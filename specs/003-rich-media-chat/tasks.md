---
description: "Task list for Feature 003-rich-media-chat: 富媒体聊天增强"
---

# Tasks: 富媒体聊天增强

**Input**: Design documents from `specs/003-rich-media-chat/` (plan.md, spec.md, data-model.md, contracts/api-contracts.md, research.md, quickstart.md)

**Tests**: AnyDrop Constitution v2.0.0 mandates tests — all new `Services/` methods require xUnit unit tests; SignalR dispatch logic requires Moq verification; cross-device push flow requires Playwright E2E.

**Organization**: Tasks grouped by user story. US1+US2 (P1) = MVP; US3–US5 (P2) = Extended MVP; US6–US7 (P3) = Full Feature.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no blocking dependencies)
- **[Story]**: User story label (US1–US7 per spec.md)
- File paths are relative to repo root

---

## Phase 1: Setup (共享基础设施)

**Purpose**: 安装新依赖、建立配置文件与 JS 文件骨架

- [ ] T001 [P] 在 `AnyDrop/AnyDrop.csproj` 中添加 `HtmlAgilityPack` NuGet 依赖（`dotnet add package HtmlAgilityPack`）
- [ ] T002 [P] 在 `AnyDrop/AnyDrop.csproj` 中添加 `SkiaSharp` + `SkiaSharp.NativeAssets.Linux` NuGet 依赖
- [ ] T003 [P] 在 `AnyDrop/appsettings.json` 和 `AnyDrop/appsettings.Development.json` 中新增 `Storage:MaxFileSizeBytes`（100MB）、`Storage:ThumbnailWidth`（320）、`LinkPreview:TimeoutSeconds`（5）、`LinkPreview:MaxResponseSizeBytes`（524288）配置节
- [ ] T004 [P] 在 `Program.cs` 中注册 `builder.Services.AddHttpClient()` 供 `LinkPreviewService` 使用
- [ ] T005 [P] 创建 `AnyDrop/wwwroot/js/dragdrop-interop.js` 骨架文件（空的 `window.AnyDropInterop` 对象，包含占位函数注释）

---

## Phase 2: Foundational (阻塞性前置任务)

**Purpose**: 新实体模型、DB Schema、后台队列基础设施和 Hub 变更——所有 User Story 均依赖此阶段完成

**⚠️ CRITICAL**: 所有 User Story 实现任务在此阶段 100% 完成前不可开始

### 2a. 新增模型与枚举

- [ ] T006 [P] 创建 `AnyDrop/Models/UploadStatus.cs`（枚举：`Completed=0, Uploading=1, Failed=2`）
- [ ] T007 [P] 创建 `AnyDrop/Models/LinkPreview.cs`（实体：Id, ShareItemId, Url, Title?, Description?, FetchedAt；`LinkPreviewDto` record；`ShareItem` 导航属性）
- [ ] T008 [P] 创建 `AnyDrop/Models/MediaMetadata.cs`（实体：Id, ShareItemId, Width?, Height?, DurationSeconds?；`MediaMetadataDto` record；`ShareItem` 导航属性）

### 2b. 扩展现有模型

- [ ] T009 修改 `AnyDrop/Models/Topic.cs`：新增 `IsPinned bool`（默认 false）和 `PinnedAt DateTimeOffset?` 字段
- [ ] T010 修改 `AnyDrop/Models/ShareItem.cs`：新增 `UploadStatus UploadStatus`（默认 `Completed`）、`ThumbnailPath string?`、`OriginalFileName string?` 字段及 `LinkPreview?`、`MediaMetadata?` 导航属性
- [ ] T011 修改 `AnyDrop/Models/TopicDto.cs`：在 `TopicDto` record 末尾添加 `bool IsPinned` 和 `DateTimeOffset? PinnedAt` 参数；更新 `Topic.ToDto()` 映射
- [ ] T012 修改 `AnyDrop/Models/ShareItemDto.cs`：在 `ShareItemDto` record 末尾添加 `UploadStatus UploadStatus`、`string? ThumbnailPath`、`string? OriginalFileName`、`LinkPreviewDto? LinkPreview`、`MediaMetadataDto? MediaMetadata`；更新 `ShareItem.ToDto()` 映射（需 Include 导航属性）

### 2c. DbContext 与 Migration

- [ ] T013 修改 `AnyDrop/Data/AnyDropDbContext.cs`：添加 `DbSet<LinkPreview>` 和 `DbSet<MediaMetadata>`；配置 `LinkPreview`（`Title` MaxLength 500、`Description` MaxLength 1000、`ShareItemId` 唯一索引）和 `MediaMetadata`（`ShareItemId` 唯一索引）实体；为 `Topic` 新增复合索引 `(IsPinned DESC, PinnedAt, LastMessageAt)`；为 `ShareItem.UploadStatus` 新增索引；为新实体配置 `DateTimeOffset` 文本转换器（与现有 `Topic`/`ShareItem` 保持一致）
- [ ] T014 执行 `dotnet ef migrations add RichMediaChatEnhancements --project AnyDrop --startup-project AnyDrop` 生成迁移文件，并在 `AnyDrop/Data/DatabaseMigrationExtensions.cs` 确认自动迁移仍有效

### 2d. 服务接口与后台基础设施

- [ ] T015 [P] 创建 `AnyDrop/Services/IBackgroundTaskQueue.cs`（泛型接口：`EnqueueAsync(Func<IServiceScope, CancellationToken, ValueTask> workItem)`，`DequeueAsync(CancellationToken)`）
- [ ] T016 创建 `AnyDrop/Services/BackgroundTaskQueue.cs`（`Channel<Func<...>>` 实现 + `BackgroundService` 消费者；消费时创建新的 DI Scope 供工作项使用）
- [ ] T017 [P] 创建 `AnyDrop/Services/ILinkPreviewService.cs`（`Task<LinkMetaResult?> FetchMetaAsync(string url, CancellationToken ct)`）
- [ ] T018 [P] 创建 `AnyDrop/Services/IThumbnailService.cs`（`Task<string?> GenerateImageThumbnailAsync(string storagePath, Guid itemId, CancellationToken ct)`；`Task<string?> GenerateVideoThumbnailAsync(string storagePath, Guid itemId, CancellationToken ct)`）
- [ ] T019 修改 `AnyDrop/Services/IShareService.cs`：新增 `Task<ShareItemDto> SendMediaAsync(Stream fileStream, string originalFileName, string mimeType, ShareContentType contentType, Guid? topicId, CancellationToken ct)`、`Task<ShareItemDto> SendAttachmentAsync(Stream fileStream, string originalFileName, string mimeType, Guid? topicId, CancellationToken ct)`、`Task<ShareItemDto> RetryUploadAsync(Guid shareItemId, CancellationToken ct)` 方法签名
- [ ] T020 修改 `AnyDrop/Services/ITopicService.cs`：新增 `Task<TopicDto> PinTopicAsync(Guid topicId, bool isPinned, CancellationToken ct)` 方法签名

### 2e. Hub 更新与 DI 注册

- [ ] T021 修改 `AnyDrop/Hubs/ShareHub.cs`：新增 `SendShareItemUpdatedAsync(ShareItemDto item)` 方法（`await Clients.All.SendAsync("ShareItemUpdated", item)`）
- [ ] T022 在 `Program.cs` 中注册新服务：`BackgroundTaskQueue` 同时注册为 `IHostedService`（`AddHostedService`）和 `IBackgroundTaskQueue`（`AddSingleton`）；`LinkPreviewService` 注册为 `ILinkPreviewService`（`AddScoped`）；`ThumbnailService` 注册为 `IThumbnailService`（`AddScoped`）

**Checkpoint**: 项目编译通过、新迁移文件存在、所有接口定义完毕 → User Story 实现可开始

---

## Phase 3: User Story 1 - 主题置顶与标题显示 (P1) 🎯 MVP

**Goal**: 聊天区顶部显示当前主题名；置顶按钮持久化改变侧边栏排序

**Independent Test**: 点击置顶 → 刷新页面 → 该主题仍在列表顶部；取消置顶 → 回归时间排序

### US1 单元测试

- [ ] T023 [P] [US1] 在 `AnyDrop.Tests.Unit/Services/TopicServiceTests.cs` 中新增 `PinTopicAsync` 测试：`PinTopicAsync_WhenUnpinned_SetsPinnedTrue`、`PinTopicAsync_WhenAlreadyPinned_UnpinsAndClearsPinnedAt`、`PinTopicAsync_MultiplePinned_SortsByPinnedAtAscending`
- [ ] T024 [P] [US1] 在 `AnyDrop.Tests.Unit/Services/TopicServiceTests.cs` 中新增 `GetAllTopicsAsync_WithMixedPinnedState_ReturnsPinnedFirst` 测试（验证 `IsPinned DESC, PinnedAt ASC, LastMessageAt DESC` 排序）

### US1 实现

- [ ] T025 [US1] 在 `AnyDrop/Services/TopicService.cs` 中实现 `PinTopicAsync`：更新 `IsPinned`（true 时同步设置 `PinnedAt=UtcNow`；false 时清空 `PinnedAt`），保存，调用 `SendTopicsUpdatedAsync` 广播
- [ ] T026 [US1] 更新 `AnyDrop/Services/TopicService.cs` 的 `GetAllTopicsAsync` 排序逻辑：`OrderByDescending(IsPinned).ThenBy(PinnedAt).ThenByDescending(LastMessageAt)`
- [ ] T027 [US1] 在 `AnyDrop/Api/TopicEndpoints.cs` 中新增 `PUT /api/v1/topics/{id}/pin` Minimal API 端点（解析 `PinTopicRequest { bool IsPinned }`，调用 `ITopicService.PinTopicAsync`，返回 `ApiEnvelope` 包装的 `TopicDto`）
- [ ] T028 [P] [US1] 在聊天区顶部（`AnyDrop/Components/Pages/Home.razor` 或独立 Header 组件）新增主题标题栏：显示当前 `TopicDto.Name`，使用 Tailwind `font-semibold text-lg border-b` 样式
- [ ] T029 [US1] 在主题标题栏新增置顶切换按钮（Heroicons `bookmark-slash` / `bookmark` inline SVG，`@onclick` 调用 `PinTopicAsync`，按 `IsPinned` 切换图标及 active 样式）；置顶后通过 `TopicsUpdated` SignalR 事件自动更新侧边栏排序

**Checkpoint**: US1 可独立测试 — 主题标题栏显示、置顶排序生效、刷新持久化

---

## Phase 4: User Story 2 - Ctrl+Enter 快捷键发送 (P1) 🎯 MVP

**Goal**: 消息输入框支持 Ctrl+Enter 快捷键触发发送，与点击发送按钮等效

**Independent Test**: 在输入框输入文本后按 Ctrl+Enter，消息发送成功，输入框清空

### US2 实现

- [ ] T030 [US2] 在 `AnyDrop/Components/Pages/Home.razor` 的消息输入 `<textarea>` 上添加 `@onkeydown="HandleKeyDown"` 处理器；在 `@code` 块中实现 `HandleKeyDown(KeyboardEventArgs e)` — 仅当 `e.CtrlKey && e.Key == "Enter"` 且输入框非空时调用现有发送逻辑；空输入时无副作用
- [ ] T031 [P] [US2] 确认 Enter（不含 Ctrl）在 `<textarea>` 中保持默认换行行为（无需代码变更，验证后在此任务打勾）

**Checkpoint**: US2 可独立验证 — Ctrl+Enter 发送；Enter 换行；空输入不发送

---

## Phase 5: User Story 3 - 超链接解析与展示 (P2)

**Goal**: 发送含 URL 的文本消息后，后台异步解析 meta 信息并通过 SignalR 实时更新气泡

**Independent Test**: 发送包含 HTTPS URL 的消息后，5 秒内气泡下方出现网页标题和描述卡片；无效 URL 仅显示原始链接

**Dependencies**: Phase 2 完成（IBackgroundTaskQueue、ShareHub.SendShareItemUpdatedAsync 可用）

### US3 单元测试

- [ ] T032 [P] [US3] 创建 `AnyDrop.Tests.Unit/Services/LinkPreviewServiceTests.cs`：`FetchMetaAsync_ValidUrl_ReturnsTitleAndDescription`、`FetchMetaAsync_Timeout_ReturnsNull`、`FetchMetaAsync_ResponseTooLarge_ReturnsNull`、`FetchMetaAsync_HttpError_ReturnsNull`、`FetchMetaAsync_NoMetaDescription_ReturnsTitleOnly`

### US3 实现

- [ ] T033 [US3] 创建 `AnyDrop/Services/LinkPreviewService.cs`（实现 `ILinkPreviewService`）：使用 `IHttpClientFactory` 创建 `HttpClient`（`Timeout=5s`）；限制读取 512KB；用 `HtmlAgilityPack` 解析 `og:title`→`<title>` 回退；`og:description`→`description` 回退；所有异常静默返回 null
- [ ] T034 [US3] 更新 `AnyDrop/Services/ShareService.cs` 的 `SendTextAsync`：发送成功后用正则检测消息文本中的第一个 HTTP/HTTPS URL；若存在则向 `IBackgroundTaskQueue` 提交链接预览工作项（工作项：解析 URL、存储 `LinkPreview` 到 DB、通过 `IHubContext<ShareHub>` 调用 `SendShareItemUpdatedAsync`）
- [ ] T035 [P] [US3] 创建 `AnyDrop/Components/Layout/LinkPreviewCard.razor`（接收 `LinkPreviewDto` 参数；Tailwind 卡片样式：`rounded-lg border p-3 text-sm`；显示标题（加粗）、描述（截断 2 行）、域名；参数为 null 时不渲染）
- [ ] T036 [US3] 在 `Home.razor` 的消息气泡渲染中，当 `item.ContentType == Link && item.LinkPreview != null` 时嵌入 `<LinkPreviewCard>` 组件
- [ ] T037 [US3] 在 `Home.razor` 的 SignalR 事件处理中新增 `ShareItemUpdated` 事件处理器：按 `Id` 找到消息列表中对应条目，更新 `LinkPreview` 字段，调用 `StateHasChanged()`

**Checkpoint**: US3 可独立验证 — 发送 URL 消息后气泡异步更新显示预览卡片

---

## Phase 6: User Story 4 - 图片/视频异步上传与进度展示 (P2)

**Goal**: 选择/拖拽图片或视频后立即自动发送，气泡即时出现显示进度，上传完成后显示缩略图，失败显示重试

**Independent Test**: 选择图片 → 气泡 500ms 内出现（Uploading 状态）→ 上传完成后显示缩略图；选择多个文件产生各自独立气泡

**Dependencies**: Phase 2 完成（IBackgroundTaskQueue、IThumbnailService 接口可用）

### US4 单元测试

- [ ] T038 [P] [US4] 在 `AnyDrop.Tests.Unit/Services/ShareServiceTests.cs` 中新增：`SendMediaAsync_ImageFile_CreatesUploadingShareItem`、`SendMediaAsync_InvalidMime_ThrowsValidationException`、`SendMediaAsync_Completion_SetsUploadStatusCompleted`
- [ ] T039 [P] [US4] 在 `AnyDrop.Tests.Unit/Services/ShareServiceTests.cs` 中新增：`RetryUploadAsync_FailedItem_ResetsToUploading`、`RetryUploadAsync_NonFailedItem_ThrowsInvalidOperationException`
- [ ] T040 [P] [US4] 创建 `AnyDrop.Tests.Unit/Services/ThumbnailServiceTests.cs`：`GenerateImageThumbnailAsync_ValidImage_ReturnsStoragePath`、`GenerateVideoThumbnailAsync_FfmpegNotAvailable_ReturnsNull`

### US4 实现 — 服务层

- [ ] T041 [US4] 实现 `AnyDrop/Services/ShareService.cs` 的 `SendMediaAsync`：验证 MIME 魔数（读取文件头字节）；校验 `ContentType` 与 MIME 类型匹配（image/* → Image，video/* → Video）；调用 `IFileStorageService.SaveFileAsync` 保存文件；创建 `ShareItem`（`UploadStatus=Uploading`）；广播 `ItemReceived`（占位气泡）；提交缩略图工作项到 `IBackgroundTaskQueue`（工作项：生成缩略图、更新 `ThumbnailPath`、`UploadStatus=Completed`、`MediaMetadata`，广播 `ShareItemUpdated`）
- [ ] T042 [US4] 实现 `AnyDrop/Services/ShareService.cs` 的 `RetryUploadAsync`：从 DB 加载 `ShareItem`，验证 `UploadStatus==Failed`（否则抛 `InvalidOperationException`）；重置为 `Uploading`；广播 `ShareItemUpdated`；重新提交上传+缩略图工作项
- [ ] T043 [US4] 创建 `AnyDrop/Services/LinkPreviewService.cs` 中的 MIME 魔数验证辅助方法（或独立 `MimeValidator.cs`）：读取文件流前 12 字节，对比 JPEG/PNG/GIF/WebP/MP4/MOV/WebM 特征字节；附件黑名单过滤 `.exe/.bat/.sh/.msi/.dll/.ps1`
- [ ] T044 [US4] 实现 `AnyDrop/Services/ThumbnailService.cs`：图片路径使用 SkiaSharp 解码 → `SKBitmap.Resize` 到 320px 宽（保持宽高比）→ 编码为 WebP → 存储；视频路径先检测 `ffmpeg` 是否在 PATH（`Process.Start` 测试调用），可用则执行 `ffmpeg -i {input} -ss 00:00:01 -vframes 1 -q:v 2 {output.jpg}`，不可用返回 null

### US4 实现 — API 端点

- [ ] T045 [US4] 在 `AnyDrop/Api/ShareItemEndpoints.cs` 中新增 `POST /api/v1/share-items/media` 端点：配置 `RequestSizeLimitAttribute`（从 `IConfiguration` 读取）；解析 multipart form-data（`IFormFile file`, `string contentType`, `Guid? topicId`）；调用 `IShareService.SendMediaAsync`；返回 `201 Created`
- [ ] T046 [US4] 在 `AnyDrop/Api/ShareItemEndpoints.cs` 中新增 `POST /api/v1/share-items/{id}/retry` 端点（调用 `IShareService.RetryUploadAsync`，返回更新后的 `ShareItemDto`；`409` 状态不符时返回错误 envelope）
- [ ] T047 [US4] 在 `AnyDrop/Api/ShareItemEndpoints.cs` 中新增 `GET /api/v1/share-items/{id}/thumbnail` 端点：读取 `ShareItem.ThumbnailPath`；若为 null 返回 `204 No Content`；否则流式返回文件（`image/webp` 或 `image/jpeg`）并设 `Cache-Control: public, max-age=86400`

### US4 实现 — 前端

- [ ] T048 [US4] 在 `AnyDrop/wwwroot/js/dragdrop-interop.js` 中实现 `getDroppedFiles(event)` 函数：从 `event.dataTransfer.files` 提取文件名、大小、类型，序列化后供 Blazor `@ondrop` 处理器使用（注意：实际文件流需通过 `InputFile` 桥接，JS 仅获取 meta 信息用于路由判断）
- [ ] T049 [P] [US4] 在 `AnyDrop/Components/Pages/Home.razor` 消息输入区新增多媒体选择按钮（Heroicons `photo` + `video-camera` SVG；`<InputFile>` 隐藏，`accept="image/*,video/*"` + `multiple`；点击按钮触发 `InputFile` 的 `click()`）
- [ ] T050 [US4] 在 `Home.razor` 的消息输入区容器上添加拖拽处理：`@ondragenter:preventDefault @ondragover:preventDefault @ondrop="HandleDrop"`；进入时添加 Tailwind `ring-2 ring-blue-400` 高亮类，离开/放置时移除；`HandleDrop` 从 `DragEventArgs` 获取信息后调用文件路由逻辑
- [ ] T051 [P] [US4] 创建 `AnyDrop/Components/Layout/UploadProgressBubble.razor`：接收 `ShareItemDto` 参数；`Uploading` 状态显示转圈动画 + 文件名 + 大小；`Failed` 状态显示错误图标 + "上传失败" + 重试按钮（`@onclick` 触发 `POST /retry`）
- [ ] T052 [US4] 在 `Home.razor` 中处理文件选择/放置后的逻辑：循环文件列表 → 判断类型（MIME → Image/Video/File）→ 调用对应 API 端点（`/media` 或 `/attachment`）→ 收到 `201` 后将 `ShareItemDto`（`UploadStatus=Uploading`）插入消息列表（渲染 `UploadProgressBubble`）
- [ ] T053 [US4] 在 `Home.razor` 的 SignalR 事件处理中扩展 `ShareItemUpdated` 处理器：更新已存在消息的 `UploadStatus`、`ThumbnailPath`、`MediaMetadata`；切换气泡渲染从 `UploadProgressBubble` 到正常气泡（显示缩略图 `<img src="/api/v1/share-items/{id}/thumbnail">`）

**Checkpoint**: US4 可独立验证 — 图片/视频上传全流程（自动发送→进度→缩略图→多文件独立气泡）

---

## Phase 7: User Story 5 - 历史记录懒加载与"回到最新"按钮 (P2)

**Goal**: 初始加载 20 条，向上滚动自动加载更多；离开底部时显示"回到最新"按钮

**Independent Test**: 50+ 条消息主题中，初始仅显示 20 条；滚动顶部加载更多；按钮点击后回到底部

**Dependencies**: Phase 2 完成（`GetTopicMessagesAsync` 接口已有，返回 `TopicMessagesResponse`）

### US5 单元测试

- [ ] T054 [P] [US5] 在 `AnyDrop.Tests.Unit/Services/TopicServiceTests.cs` 中新增：`GetTopicMessagesAsync_WithoutCursor_ReturnsLatest20`、`GetTopicMessagesAsync_WithCursor_ReturnsEarlierMessages`、`GetTopicMessagesAsync_AllLoaded_HasMoreFalse`

### US5 实现

- [ ] T055 [US5] 将 `Home.razor` 初始消息加载从 `GetRecentAsync(50)` 改为 `GetTopicMessagesAsync(topicId, limit=20, before=null)` 并存储 `NextCursor` 和 `HasMore` 状态变量
- [ ] T056 [US5] 在 `AnyDrop/wwwroot/js/dragdrop-interop.js` 中实现 `setupScrollSentinel(sentinelElementRef, dotNetRef, methodName)` 函数：用 `IntersectionObserver` 监听哨兵元素；进入视口时调用 `dotNetRef.invokeMethodAsync(methodName)`
- [ ] T057 [US5] 在 `AnyDrop/wwwroot/js/dragdrop-interop.js` 中实现 `setupScrollTracking(containerRef, dotNetRef)` 函数：监听 scroll 事件，距底部 > 200px 时调用 `dotNetRef.invokeMethodAsync("OnScrolledUp", true)`，否则调用 `false`
- [ ] T058 [US5] 在 `Home.razor` 消息列表顶部添加哨兵 `<div>` 元素（id="scroll-sentinel"）；`OnAfterRenderAsync` 时调用 `setupScrollSentinel` JS 函数并传入 `DotNetObjectReference.Create(this)`
- [ ] T059 [US5] 在 `Home.razor @code` 中实现 `[JSInvokable] LoadMoreAsync()`：若 `HasMore && !IsLoadingMore` 则调用 `GetTopicMessagesAsync(cursor=NextCursor, limit=20)` → 将结果前插到消息列表（`Messages.InsertRange(0, olderMessages)`）→ 更新 `NextCursor`、`HasMore`
- [ ] T060 [P] [US5] 在消息列表顶部（HasMore=false 时）渲染"已加载全部消息"Tailwind badge（`text-xs text-gray-400 text-center py-2`）
- [ ] T061 [US5] 在 `Home.razor` 添加 `IsScrolledUp bool` 状态变量；`OnAfterRenderAsync` 时调用 `setupScrollTracking`；添加 `[JSInvokable] OnScrolledUp(bool scrolledUp)` 方法更新状态并 `StateHasChanged()`
- [ ] T062 [P] [US5] 在 `Home.razor` 消息列表右下角添加"回到最新消息"浮动按钮（`position: fixed`，条件渲染 `@if(IsScrolledUp)`，点击调用 `scrollToBottom` JS 函数）
- [ ] T063 [P] [US5] 在 `AnyDrop/wwwroot/js/dragdrop-interop.js` 中实现 `scrollToBottom(containerRef)` 函数（`containerRef.scrollTop = containerRef.scrollHeight`，添加 `behavior: 'smooth'`）

**Checkpoint**: US5 可独立验证 — 初始 20 条、滚顶加载更多、"回到最新"按钮行为

---

## Phase 8: User Story 6 - 多媒体消息点击预览与信息展示 (P3)

**Goal**: 点击图片/视频气泡弹出全屏 Modal，显示原图/视频播放器、文件元信息和下载按钮

**Independent Test**: 点击图片气泡 → Modal 弹出显示原图 + 文件名/大小/日期/分辨率 + 下载按钮可用

**Dependencies**: US4 完成（`ShareItemDto` 含 `MediaMetadata`，缩略图端点可用）

### US6 实现

- [ ] T064 [US6] 在 `AnyDrop/Api/ShareItemEndpoints.cs` 中新增 `GET /api/v1/share-items/{id}/file` 端点：验证 `ShareItem` 存在且 `ContentType` 为文件类型；从 `IFileStorageService.GetFileAsync` 读取流；返回 `File(stream, mimeType)` 并设置 `Content-Disposition: attachment; filename="{OriginalFileName}"`（使用 RFC 5987 编码处理文件名特殊字符）
- [ ] T065 [P] [US6] 创建 `AnyDrop/Components/Layout/MediaPreviewModal.razor`：接收 `ShareItemDto?` 参数；`null` 时不渲染；Tailwind `fixed inset-0 bg-black/80 flex items-center justify-center` 遮罩；图片类型渲染 `<img src="..." class="max-w-full max-h-[85vh] object-contain">`；视频类型渲染 `<video src="..." controls class="max-w-full max-h-[85vh]">`；底部信息栏显示文件名、大小（格式化 KB/MB）、上传日期、分辨率（图片）或时长（视频）
- [ ] T066 [US6] 在 `MediaPreviewModal.razor` 中添加下载按钮（`<a href="/api/v1/share-items/{Id}/file" download>` 链接）和关闭按钮（点击遮罩或 × 按钮清空 `ShareItemDto` 参数）
- [ ] T067 [US6] 在 `Home.razor` 消息列表中，为 `ContentType==Image` 和 `ContentType==Video` 且 `UploadStatus==Completed` 的气泡包裹 `@onclick="() => OpenMediaPreview(item)"`；添加 `MediaPreviewModal` 组件绑定 `SelectedItem` 状态变量

**Checkpoint**: US6 可独立验证 — 图片/视频气泡点击→Modal 显示→下载有效

---

## Phase 9: User Story 7 - 附件异步上传与预览 (P3)

**Goal**: 附件选择/拖拽自动发送，气泡显示进度；点击弹出 Modal 含文本内容预览和下载

**Independent Test**: 拖拽 PDF → 气泡自动出现进度 → 成功后显示附件图标 → 点击 Modal 展示文件信息和下载按钮

**Dependencies**: US4 完成（拖拽基础设施、`UploadProgressBubble` 可用）；T064 完成（`/file` 下载端点）

### US7 单元测试

- [ ] T068 [P] [US7] 在 `AnyDrop.Tests.Unit/Services/ShareServiceTests.cs` 中新增：`SendAttachmentAsync_DangerousExtension_ThrowsValidationException`（`.exe`/`.bat` 被拒绝）、`SendAttachmentAsync_ValidFile_CreatesUploadingShareItem`、`SendAttachmentAsync_MagicBytesMismatch_ThrowsValidationException`

### US7 实现

- [ ] T069 [US7] 实现 `AnyDrop/Services/ShareService.cs` 的 `SendAttachmentAsync`：对文件名做危险扩展黑名单检查（`.exe/.bat/.sh/.msi/.dll/.ps1/.cmd` 等）；读取文件头魔数做基本校验（排除已知可执行格式）；调用 `IFileStorageService.SaveFileAsync`；创建 `ShareItem`（`ContentType=File`，`UploadStatus=Uploading`）；广播 `ItemReceived`；后台工作项：更新 `UploadStatus=Completed`，广播 `ShareItemUpdated`
- [ ] T070 [US7] 在 `AnyDrop/Api/ShareItemEndpoints.cs` 中新增 `POST /api/v1/share-items/attachment` 端点（与 `/media` 端点类似；`contentType` 固定为 `File`；调用 `SendAttachmentAsync`）
- [ ] T071 [P] [US7] 在 `Home.razor` 新增独立附件按钮（Heroicons `paper-clip` SVG；`<InputFile>` 隐藏，不限 `accept` 类型；`multiple`）；文件选择后调用 `POST /api/v1/share-items/attachment`
- [ ] T072 [US7] 更新 `Home.razor` 的拖拽放置处理器 `HandleDrop`：根据 MIME 类型路由 — `image/*` 或 `video/*` 调用 `/media`；其余调用 `/attachment`
- [ ] T073 [P] [US7] 创建 `AnyDrop/Components/Layout/AttachmentPreviewModal.razor`：接收 `ShareItemDto?` 参数；显示附件图标（Heroicons `document` SVG）+ 文件名 + 大小 + 上传日期；下载按钮；`txt/md/csv` 类型时：通过 `IJSRuntime` 或 API 读取文件内容，`<pre>` 渲染（限制 1MB，超出截断并提示）
- [ ] T074 [US7] 在 `Home.razor` 消息列表中，为 `ContentType==File` 且 `UploadStatus==Completed` 的气泡绑定 `@onclick="() => OpenAttachmentPreview(item)"`；添加 `AttachmentPreviewModal` 组件绑定状态变量

**Checkpoint**: US7 可独立验证 — 附件上传全流程（自动发送→进度→完成气泡→Modal 预览→下载）

---

## Final Phase: Polish & 横切关注点

**Purpose**: E2E 测试、SignalR Moq 验证、Dockerfile 更新、最终质量收尾

- [ ] T075 [P] 在 `AnyDrop.Tests.Unit/Services/ShareServiceTests.cs` 中添加 Moq 验证：`SendMediaAsync_OnCompletion_CallsSendShareItemUpdated`（验证 `IHubContext<ShareHub>` 的 `Clients.All.SendAsync("ShareItemUpdated", ...)` 被调用）
- [ ] T076 [P] 在 `AnyDrop.Tests.E2E/Tests/RichMediaTests.cs` 中创建 E2E 测试：`ImageUpload_SelectFile_BubbleAppearsWithinHalfSecond_ThenShowsThumbnail`（上传图片 → 验证气泡 500ms 内出现 → 等待缩略图显示）
- [ ] T077 [P] 在 `AnyDrop.Tests.E2E/Tests/RichMediaTests.cs` 中创建 E2E 测试：`MediaPreview_ClickImageBubble_ModalOpensWithDownloadButton`（点击图片气泡 → Modal 出现 → 下载链接可访问）
- [ ] T078 [P] 在 `AnyDrop.Tests.E2E/Tests/RichMediaTests.cs` 中创建 E2E 测试：`TopicPin_PinTopic_PersistsAfterRefresh`（置顶主题 → 刷新页面 → 该主题仍在列表顶部）
- [ ] T079 [P] 在 `AnyDrop.Tests.E2E/Tests/RichMediaTests.cs` 中创建 E2E 测试：`CtrlEnter_SendMessage_InputClears`（输入文本 → Ctrl+Enter → 消息出现 → 输入框清空）
- [ ] T080 在 `Dockerfile`（根目录）中添加 FFmpeg 安装层：`RUN apt-get update && apt-get install -y ffmpeg && rm -rf /var/lib/apt/lists/*`（置于 dotnet publish 阶段之前的 build stage 或运行时 stage）
- [ ] T081 [P] 验证所有新 Razor 组件的 Tailwind CSS 类被 `@source "../**/*.razor"` 正确扫描（运行 `npm run build` 检查 `tailwind.css` 输出）
- [ ] T082 [P] 检查并确认新实体（`LinkPreview`、`MediaMetadata`）的 `DateTimeOffset` 字段均配置了与现有 `Topic`/`ShareItem` 一致的文本格式转换器（`"O"` 格式）
- [ ] T083 [P] 运行 `dotnet build` + `dotnet test AnyDrop.Tests.Unit` 确认无编译错误和单元测试全部通过

---

## 依赖关系图

```
Phase 1 (Setup)
    ↓
Phase 2 (Foundation) — 全部 User Story 均阻塞于此
    ├── US1 (Phase 3) ← P1 MVP [独立]
    │       ↓ (可并行)
    ├── US2 (Phase 4) ← P1 MVP [独立]
    │
    ├── US3 (Phase 5) ← P2 [依赖 Phase 2 的 BackgroundTaskQueue]
    │
    ├── US4 (Phase 6) ← P2 [依赖 Phase 2 的 IThumbnailService + BackgroundTaskQueue]
    │       ↓
    ├── US5 (Phase 7) ← P2 [独立，可与 US3/US4 并行]
    │
    ├── US6 (Phase 8) ← P3 [依赖 US4 完成]
    │
    └── US7 (Phase 9) ← P3 [依赖 US4 基础设施 + T064(/file 端点)]
            ↓
    Final Phase (Polish)
```

**并行执行示例（US3+US4+US5 阶段）**:
- 开发者 A：实现 LinkPreviewService（T033）+ 更新 ShareService URL 检测（T034）
- 开发者 B：实现 ThumbnailService（T044）+ SendMediaAsync（T041）
- 开发者 C：实现 JS IntersectionObserver 脚本（T056-T057）+ Home.razor 懒加载逻辑

---

## 实现策略

**MVP 优先**（建议先完成 T001–T031）：
- Phase 1 + Phase 2 + US1（置顶）+ US2（Ctrl+Enter）
- MVP 通过后即可提供 P1 功能演示，同时 P2/P3 任务并行推进

**增量交付**：
- Iteration 1 (MVP): T001–T031（~23 个任务，Setup + Foundation + US1 + US2）
- Iteration 2 (Extended): T032–T063（~32 个任务，US3 链接预览 + US4 媒体上传 + US5 懒加载）
- Iteration 3 (Full): T064–T083（~20 个任务，US6 Modal 预览 + US7 附件 + Polish）

---

## 汇总

- **总任务数**: 83 个（T001–T083）
- **按 User Story 分布**：
  - Phase 1 Setup: 5 个
  - Phase 2 Foundation: 17 个
  - US1 主题置顶: 7 个（含 2 个单测）
  - US2 Ctrl+Enter: 2 个
  - US3 链接预览: 6 个（含 1 个单测）
  - US4 图片/视频上传: 16 个（含 3 个单测）
  - US5 历史记录懒加载: 10 个（含 1 个单测）
  - US6 多媒体 Modal: 4 个
  - US7 附件上传: 7 个（含 1 个单测）
  - Final Polish: 9 个（含 E2E 测试 4 个）
- **并行机会**：Setup 阶段全部可并行；Foundation 的 2a（新建模型）3 个任务可并行；US3/US4/US5 在 Foundation 完成后可三路并行
- **各 Story 独立测试标准**（可用于验收）：
  - US1：置顶后刷新 → 主题排最前
  - US2：Ctrl+Enter → 消息出现 + 输入框清空
  - US3：发送 URL → 5s 内气泡显示预览卡片
  - US4：选择/拖拽图片 → 500ms 内气泡出现 → 上传完成后显示缩略图
  - US5：50+ 条消息主题 → 初始 20 条 → 滚顶加载更多
  - US6：点击图片气泡 → Modal 300ms 内打开 → 下载有效
  - US7：拖拽附件 → 自动发送 → 完成后点击 Modal 可查看信息
- **建议 MVP 范围**：完成 T001–T031（US1 + US2 + Foundation）即可演示 P1 功能

---
