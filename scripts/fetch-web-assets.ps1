<#
.SYNOPSIS
    把前端运行时依赖从公共 CDN 下载到本地，实现完全自托管。

.DESCRIPTION
    此前页面会从两个第三方 CDN 加载资源：

      * cdn.jsdelivr.net 上的 Sortable.min.js —— 可执行脚本，且没有 SRI 校验；
      * fonts.googleapis.com / fonts.gstatic.com 上的字体与图标字体。

    这两者与本项目「私有、自托管、不依赖第三方云服务」的定位相矛盾：
    每次打开页面都会向第三方暴露访问行为，CDN 被墙或不可达时页面还会降级。
    由本脚本把它们落到 wwwroot 下，页面只引用同源资源。

    图标字体按源码中实际出现的图标名做子集化（Google Fonts 的 icon_names 参数），
    否则 Material Symbols 的完整可变字体接近 4 MB。子集列表由脚本从源码提取，
    因此新增图标后重新运行本脚本即可，不会漏字。

.EXAMPLE
    pwsh scripts/fetch-web-assets.ps1
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = (Join-Path $PSScriptRoot '..'),
    [string] $SortableVersion = '1.15.6'
)

$ErrorActionPreference = 'Stop'

$webRoot = Join-Path $RepoRoot 'AnyDrop/wwwroot'
$fontsDir = Join-Path $webRoot 'fonts'
$cssDir = Join-Path $webRoot 'css'
$jsDir = Join-Path $webRoot 'js'

# 用现代浏览器 UA 请求，否则 Google Fonts 会退回 woff（体积更大、兼容性更差）
$userAgent = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36'

foreach ($dir in @($fontsDir, $cssDir, $jsDir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

# ── 1. 从源码提取用到的 Material Symbols 图标名 ────────────────────────────────
Write-Host '收集源码中的图标名…'
$sourceFiles = Get-ChildItem -Path (Join-Path $RepoRoot 'AnyDrop') -Recurse -Include '*.razor', '*.razor.cs' -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

$iconNames = [System.Collections.Generic.HashSet[string]]::new()

foreach ($file in $sourceFiles) {
    $text = Get-Content $file.FullName -Raw

    # <span class="material-symbols-outlined ...">icon_name</span>
    foreach ($m in [regex]::Matches($text, 'material-symbols-outlined[^>]*>\s*([a-z0-9_]+)\s*<')) {
        [void]$iconNames.Add($m.Groups[1].Value)
    }

    # 图标选择器里的数组，例如 _availableIcons = [ "chat_bubble", "bookmark", ... ];
    foreach ($m in [regex]::Matches($text, '(?s)_availableIcons\s*=\s*\[(.*?)\]')) {
        foreach ($q in [regex]::Matches($m.Groups[1].Value, '"([a-z0-9_]+)"')) {
            [void]$iconNames.Add($q.Groups[1].Value)
        }
    }
}

if ($iconNames.Count -eq 0) {
    throw '未能从源码中提取到任何图标名，脚本可能已与代码结构脱节。'
}

# Google Fonts 要求 icon_names 按字母序排列
$iconList = ($iconNames | Sort-Object) -join ','
Write-Host "  共 $($iconNames.Count) 个图标"

# ── 2. 下载字体 CSS 与字体文件 ────────────────────────────────────────────────
$fontSources = @(
    'https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&family=Manrope:wght@700;800&display=swap',
    "https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined:opsz,wght,FILL,GRAD@20..48,100..700,0..1,-50..200&icon_names=$iconList"
)

Get-ChildItem $fontsDir -File | Remove-Item -Force

$urlMap = @{}
$usedNames = @{}
$cssBuilder = [System.Text.StringBuilder]::new()

foreach ($source in $fontSources) {
    Write-Host "下载 CSS：$source"
    $css = (Invoke-WebRequest $source -UserAgent $userAgent -TimeoutSec 60).Content

    foreach ($match in [regex]::Matches($css, 'https://fonts\.gstatic\.com/[^)''"\s]+')) {
        $remote = $match.Value
        if (-not $urlMap.ContainsKey($remote)) {
            $fileName = [System.IO.Path]::GetFileName(([Uri]$remote).AbsolutePath)
            if ([string]::IsNullOrWhiteSpace($fileName)) { $fileName = 'font' }

            # 部分 gstatic 地址（如 Material Symbols 的 /l/font?kit=…）末段没有扩展名。
            # 缺扩展名会让静态文件中间件给出错误的 Content-Type，浏览器在
            # X-Content-Type-Options: nosniff 下可能直接拒绝加载字体。
            if ([string]::IsNullOrEmpty([System.IO.Path]::GetExtension($fileName))) {
                $fileName += '.woff2'
            }

            if ($usedNames.ContainsKey($fileName)) {
                $sha = [System.Security.Cryptography.SHA256]::Create()
                $hash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($remote))) -replace '-', '').Substring(0, 10).ToLower()
                $fileName = "$hash-$fileName"
            }

            $usedNames[$fileName] = $true
            Invoke-WebRequest $remote -OutFile (Join-Path $fontsDir $fileName) -TimeoutSec 120
            $urlMap[$remote] = "/fonts/$fileName"
        }

        $css = $css.Replace($remote, $urlMap[$remote])
    }

    [void]$cssBuilder.AppendLine("/* 由 scripts/fetch-web-assets.ps1 自托管；原始来源：$source */")
    [void]$cssBuilder.AppendLine($css)
}

[System.IO.File]::WriteAllText((Join-Path $cssDir 'fonts.css'), $cssBuilder.ToString())

# ── 3. 下载 Sortable.js ───────────────────────────────────────────────────────
$sortableUrl = "https://cdn.jsdelivr.net/npm/sortablejs@$SortableVersion/Sortable.min.js"
Write-Host "下载 $sortableUrl"
Invoke-WebRequest $sortableUrl -OutFile (Join-Path $jsDir 'sortable.min.js') -TimeoutSec 60

# ── 4. 汇总 ───────────────────────────────────────────────────────────────────
$fontBytes = (Get-ChildItem $fontsDir -File | Measure-Object -Property Length -Sum).Sum
Write-Host ''
Write-Host "字体文件：$((Get-ChildItem $fontsDir -File).Count) 个，共 $([math]::Round($fontBytes / 1KB)) KB"
Write-Host "sortable.min.js：$([math]::Round((Get-Item (Join-Path $jsDir 'sortable.min.js')).Length / 1KB)) KB"

$remaining = Select-String -Path (Join-Path $cssDir 'fonts.css') -Pattern 'fonts\.gstatic\.com|fonts\.googleapis\.com' |
    Where-Object { $_.Line -notmatch '^\s*/\*' }
if ($remaining) {
    throw 'fonts.css 中仍有未替换的远程地址，自托管不完整。'
}

Write-Host '完成：页面不再需要访问任何第三方 CDN。'
