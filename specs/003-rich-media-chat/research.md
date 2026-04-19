# Research: 富媒体聊天增强 (003-rich-media-chat)

**Generated**: 2026-04-19  
**Phase**: 0 — 技术选型与最佳实践汇总

---

## 1. 主题置顶排序策略

**Decision**: `Topic` 实体新增 `IsPinned bool` 和 `PinnedAt DateTimeOffset?` 字段；侧边栏排序 SQL 为 `ORDER BY IsPinned DESC, PinnedAt ASC NULLS LAST, LastMessageAt DESC NULLS LAST`。

**Rationale**: 已有 `SortOrder` 字段负责手动排序，置顶逻辑独立字段可与手动排序叠加，互不干扰。多主题均置顶时按置顶时间升序（先置顶的排更前），符合用户直觉。

**Alternatives considered**:
- 复用 `SortOrder` 将置顶主题设为负数 → 与现有手动排序语义冲突，放弃
- 独立置顶排序表 → 过度设计，放弃

---

## 2. Ctrl+Enter 快捷键实现

**Decision**: 在 Blazor Razor 组件中使用 `@onkeydown` 事件结合 `KeyboardEventArgs` 检测 `ctrlKey && key == "Enter"`；不依赖 JS interop，Blazor Server 原生支持。

**Rationale**: Blazor Server 的 DOM 事件可直接携带 `ctrlKey`/`shiftKey` 修饰键，无需额外 JS。`@onkeydown:preventDefault` 仅在条件满足时通过代码控制，避免阻止正常 Enter 换行。

**Alternatives considered**:
- JS `addEventListener` + `DotNetObjectReference` → 增加互操作复杂度，无必要

---

## 3. 超链接 Meta 解析

**Decision**: 使用 .NET 内置 `HttpClient`（通过 `IHttpClientFactory` 注册）在服务端抓取 HTML，用 `HtmlAgilityPack`（NuGet）解析 `<meta>` 标签，提取 `og:title`、`og:description`、`<title>`、`description`。解析结果存入 `LinkPreview` 实体并通过 SignalR 推送更新对应气泡。

**Rationale**:
- `HtmlAgilityPack` 是 .NET 生态最成熟的 HTML 解析库，无运行时依赖，MIT 许可
- 服务端解析避免 CORS 问题及 CSP 限制
- 通过 SignalR 推送更新，气泡实时刷新无需用户操作

**Alternatives considered**:
- `AngleSharp` → 功能更全但体积更大，本场景只需 meta 解析，HtmlAgilityPack 已足够
- 前端 JS fetch + proxy → 违反服务端解析原则，放弃

**Implementation notes**:
- 设置 `HttpClient.Timeout = 5s`，超时或异常时静默降级（仅显示原始链接）
- 响应体读取上限 512KB，防止大文件 OOM
- 解析任务通过 `IBackgroundTaskQueue` + `IHostedService` 异步处理，不阻塞消息发送响应
- `LinkPreview` 存 DB，刷新后仍可显示

---

## 4. 文件异步上传与进度推送

**Decision**: 采用"占位消息"模式：
1. 用户选择/拖拽文件 → 前端立即调用 `SendFileAsync`（传文件流），Service 层先写入 `ShareItem`（`UploadStatus=Uploading`）并广播占位消息
2. 后台异步将文件流存储至 `IFileStorageService`，更新 `UploadStatus` 及 `FilePath`
3. 通过 SignalR 推送 `ShareItemUpdated` 事件，前端更新对应气泡状态

**Rationale**: Blazor Server 通过 `InputFile.OnChange` 可获取文件流，直接管道到存储层，无需 Base64 编码。占位消息让用户立即看到气泡，异步完成后刷新，体验与主流 IM 一致。

**Implementation notes**:
- `InputFile` 的 `maxAllowedSize` 设为 100MB（通过配置）
- 拖拽：在消息框容器绑定 `@ondragover:preventDefault` + `@ondrop`，从 `DragEventArgs` 获取文件（需 JS interop 获取拖拽文件对象）
- 上传进度：Blazor Server 中无法原生监听字节级进度；使用"上传中 → 完成/失败"两态即可，避免过度工程化
- 重试：`RetryUploadAsync(shareItemId)` 从 DB 取出记录，重置状态，重新上传

---

## 5. 图片缩略图 & 视频预览帧生成（后台任务）

**Decision**:
- **图片缩略图**：使用 `SkiaSharp`（跨平台 2D 图形库，NuGet）生成，宽度 320px 保持宽高比，保存为 WebP
- **视频预览帧**：使用 FFmpeg CLI（`Process.Start`），命令：`ffmpeg -i <input> -ss 00:00:01 -vframes 1 -q:v 2 <output.jpg>`；FFmpeg 不可用时降级（无缩略图，显示视频图标）
- 均通过 `IHostedService` + `Channel<ThumbnailJob>` 实现后台队列处理

**Rationale**: SkiaSharp 是 .NET 生态最佳跨平台图像处理选择（无 GDI+ 依赖，Linux/Alpine 友好）。FFmpeg 为视频处理标准工具，通过进程调用隔离安全风险。

**Alternatives considered**:
- `ImageSharp` → 商业许可限制，放弃
- `System.Drawing` → 仅 Windows，不满足 Alpine Docker 要求，放弃
- FFmpeg.NET / Xabe.FFmpeg → 封装层，增加依赖，直接调用 CLI 更简洁

---

## 6. 历史记录懒加载（分页）

**Decision**: 使用基于游标的分页（Cursor Pagination）：以 `CreatedAt DESC` 排序，游标为最早一条消息的 `CreatedAt`。`GetTopicMessagesAsync(topicId, limit=20, before=null)` 已在 `ITopicService` 中定义，直接扩展实现。

前端使用 `IntersectionObserver`（JS interop）监听消息列表顶部哨兵元素；当哨兵进入视口时触发加载更多。"回到最新"按钮通过 `IJSRuntime.InvokeVoidAsync("scrollToBottom", elementRef)` 实现平滑滚动。

**Rationale**: 游标分页在消息插入场景下稳定性优于 offset，不会因新消息插入导致重复/跳过。`TopicMessagesResponse` 已含 `HasMore` 和 `NextCursor` 字段，无需改动接口。

**Implementation notes**:
- 初始加载 20 条，每次向上触发加载 20 条（可配置）
- 新消息实时推送仍通过 SignalR，不经过分页接口
- "回到最新"按钮：监听消息容器 scroll 事件（JS），距底部 >200px 时显示

---

## 7. 多媒体 Modal 预览

**Decision**: 纯 Blazor 组件实现 Modal，不引入第三方 JS Modal 库。结构：`MediaPreviewModal.razor`（图片/视频预览）+ `AttachmentPreviewModal.razor`（附件预览）。使用 Tailwind CSS `fixed inset-0` 实现全屏遮罩。

**Implementation notes**:
- 图片：`<img>` 标签，`max-w-full max-h-screen object-contain`
- 视频：原生 `<video controls>` 标签，浏览器原生播放
- 附件文本预览：通过 `IFileStorageService.GetFileAsync` 读取内容，`StreamReader` 限读 1MB，超出时截断并提示
- 文件信息从 `ShareItemDto` + `MediaMetadata` 合并展示
- 下载按钮：链接至 `/api/v1/files/{id}/download`，服务端返回 `Content-Disposition: attachment`

---

## 8. 拖拽上传

**Decision**: 在消息输入框容器（`<div>`）绑定：
- `@ondragover:preventDefault` + `@ondragenter:preventDefault`（阻止浏览器默认行为）
- `@ondrop` → 通过 JS interop 获取 `DataTransfer.files`，转换为 `IBrowserFile[]`，复用文件上传逻辑

**Implementation notes**:
- Blazor Server 的 `DragEventArgs` 不直接暴露文件，需 JS helper 函数将 `DataTransfer.files` 桥接为可用对象
- 拖拽高亮：`@ondragenter` 时添加 Tailwind `ring-2 ring-brand`，`@ondragleave`/`@ondrop` 时移除
- 输入框有文字时拖入文件：文件独立发送，文字内容保留（不清空输入框）

---

## 9. 安全 & 文件类型校验

**Decision**:
- MIME 类型校验：服务端读取文件魔数（Magic Bytes）校验实际类型，不信任客户端传递的 MIME
- 图片允许：`image/jpeg`、`image/png`、`image/gif`、`image/webp`
- 视频允许：`video/mp4`、`video/quicktime`、`video/webm`
- 附件：黑名单过滤危险可执行类型（`.exe`、`.bat`、`.sh`、`.msi`、`.dll` 等）
- 文件大小上限：通过 `IConfiguration["Storage:MaxFileSizeBytes"]` 配置，默认 100MB

**Rationale**: 魔数校验防止扩展名伪造绕过，黑名单比白名单对附件更实用（允许大量未知文档格式）。

---

## 10. 新增 NuGet 依赖汇总

| 包 | 用途 | 许可 |
|----|------|------|
| `HtmlAgilityPack` | 服务端 HTML meta 解析 | MIT |
| `SkiaSharp` | 图片缩略图生成（跨平台） | MIT |
| `SkiaSharp.NativeAssets.Linux` | Alpine Docker 运行时支持 | MIT |

FFmpeg 通过 Docker 基础镜像安装（`apt-get install -y ffmpeg`），不作为 NuGet 依赖。

---

## 11. EF Core Migration 变更清单

| 表 | 变更 |
|----|------|
| `Topics` | 新增 `IsPinned` (bool, NOT NULL, DEFAULT 0), `PinnedAt` (text nullable) |
| `ShareItems` | 新增 `ThumbnailPath` (text nullable), `OriginalFileName` (text nullable), `UploadStatus` (int, NOT NULL, DEFAULT 0) |
| `LinkPreviews`（新表） | `Id`, `ShareItemId` (FK), `Url`, `Title`, `Description`, `FetchedAt` |
| `MediaMetadata`（新表） | `Id`, `ShareItemId` (FK), `Width`, `Height`, `DurationSeconds` |

---

## NEEDS CLARIFICATION — 全部已解决

所有规格歧义已在 `/speckit.clarify` 阶段解决（见 spec.md Clarifications 节），无遗留项。
