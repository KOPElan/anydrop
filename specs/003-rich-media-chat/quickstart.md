# 开发者快速启动指南 (003-rich-media-chat)

**Generated**: 2026-04-19  
**Feature**: 富媒体聊天增强

---

## 前置条件

| 工具 | 版本要求 | 说明 |
|------|----------|------|
| .NET SDK | 10.0+ | `dotnet --version` 验证 |
| Node.js | 18+ | Tailwind CSS CLI 构建需要 |
| Docker | 20.10+ | 容器化运行 |
| FFmpeg | 任意新版 | 可选，用于视频缩略图生成 |

---

## 1. 安装新 NuGet 依赖

在 `AnyDrop/` 项目目录下执行：

```bash
dotnet add package HtmlAgilityPack
dotnet add package SkiaSharp
dotnet add package SkiaSharp.NativeAssets.Linux
```

验证安装（`AnyDrop.csproj` 中应出现）：
```xml
<PackageReference Include="HtmlAgilityPack" Version="1.11.*" />
<PackageReference Include="SkiaSharp" Version="2.88.*" />
<PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="2.88.*" />
```

---

## 2. FFmpeg 安装（可选）

### Linux / Docker
在 `Dockerfile` 中添加：
```dockerfile
RUN apt-get update && apt-get install -y ffmpeg && rm -rf /var/lib/apt/lists/*
```

### Windows（开发环境）
1. 下载 FFmpeg：https://ffmpeg.org/download.html
2. 将 `ffmpeg.exe` 所在目录加入系统 `PATH`
3. 验证：`ffmpeg -version`

> **降级策略**：若 `ffmpeg` 命令不在 PATH，视频缩略图功能自动禁用，视频气泡显示默认图标，不影响其他功能。

---

## 3. 运行 EF Core Migration

创建并应用新迁移（本 Feature 涉及 4 处 Schema 变更）：

```bash
# 在 repo 根目录执行
cd d:\Document\Code\anydrop

dotnet ef migrations add RichMediaChatEnhancements \
  --project AnyDrop \
  --startup-project AnyDrop

dotnet ef database update \
  --project AnyDrop \
  --startup-project AnyDrop
```

---

## 4. 环境变量配置

在 `appsettings.Development.json` 或 `.env`（容器环境）中配置：

```json
{
  "Storage": {
    "BasePath": "D:\\anydrop-storage",
    "MaxFileSizeBytes": 104857600,
    "ThumbnailWidth": 320
  },
  "LinkPreview": {
    "TimeoutSeconds": 5,
    "MaxResponseSizeBytes": 524288
  }
}
```

| 变量 | 默认值 | 说明 |
|------|--------|------|
| `Storage:BasePath` | `./storage` | 文件存储根目录（容器中应挂载 Volume） |
| `Storage:MaxFileSizeBytes` | `104857600` (100MB) | 单文件上传上限 |
| `Storage:ThumbnailWidth` | `320` | 缩略图宽度（像素，保持宽高比） |
| `LinkPreview:TimeoutSeconds` | `5` | 链接预览抓取超时 |
| `LinkPreview:MaxResponseSizeBytes` | `524288` (512KB) | 限制抓取响应体大小 |

---

## 5. 本地运行

```bash
# 启动应用（同时 watch Tailwind CSS）
cd d:\Document\Code\anydrop
npm run watch &   # Tailwind 热更新
dotnet run --project AnyDrop
```

访问 `http://localhost:5002`

---

## 6. 测试

```bash
# 单元测试
dotnet test AnyDrop.Tests.Unit

# E2E 测试（需应用在运行）
dotnet test AnyDrop.Tests.E2E
```

---

## 7. 常见问题

**Q: 图片上传后缩略图不显示？**  
A: SkiaSharp 在 Alpine Linux 中需要 `SkiaSharp.NativeAssets.Linux` 包。确认 NuGet 包已安装，重新 `docker build`。

**Q: 视频上传成功但无预览帧？**  
A: 检查 FFmpeg 是否在 PATH（`ffmpeg -version`）。Docker 环境确认 Dockerfile 中有 `apt-get install -y ffmpeg`。

**Q: 链接预览不加载？**  
A: 检查服务器是否可访问外网。内网环境下链接预览功能静默降级（不显示预览卡片）。

**Q: 文件上传报 413 错误？**  
A: 检查 `Storage:MaxFileSizeBytes` 配置，同时检查 Kestrel 的 `MaxRequestBodySize`（`Program.cs` 中配置与业务上限一致）。
