# Implementation Plan: 富媒体聊天增强

**Branch**: `003-rich-media-chat` | **Date**: 2026-04-19 | **Spec**: [specs/003-rich-media-chat/spec.md](./spec.md)  
**Input**: Feature specification from `specs/003-rich-media-chat/spec.md`

---

## Summary

为 AnyDrop 聊天界面增加富媒体能力：主题置顶、Ctrl+Enter 快捷发送、超链接卡片预览、异步文件拖拽上传（含进度/重试）、分页懒加载历史记录、媒体预览 Modal，以及附件管理。  

技术方案：在 Blazor Server + SignalR 已有架构上扩展，新增 `LinkPreview`/`MediaMetadata` 实体，引入 `SkiaSharp`（图片缩略图）+ `HtmlAgilityPack`（链接 meta 解析），通过后台队列（`IHostedService` + `Channel<T>`）处理耗时任务，全程通过 SignalR 推送状态更新，无前端框架变更。

---

## Technical Context

**Language/Version**: C# 13, .NET 10  
**Primary Dependencies**: Blazor Server (Interactive Server), EF Core + SQLite, SignalR, `HtmlAgilityPack`, `SkiaSharp`, `SkiaSharp.NativeAssets.Linux`  
**Storage**: SQLite（`AnyDropDbContext`），本地文件系统（`IFileStorageService`/`LocalFileStorageService`）  
**Testing**: xUnit + FluentAssertions + Moq（单元），Playwright（E2E）  
**Target Platform**: Linux (Alpine/Debian) + Windows 开发环境，Docker 容器  
**Project Type**: Blazor Server Web App（已有，本 Feature 为功能扩展）  
**Performance Goals**: 文件上传不阻塞 UI；链接预览在 5s 内推送或静默降级；缩略图生成在 10s 内完成  
**Constraints**: 无 WASM，无 MVC Controller，无 Bootstrap/Fluent UI；单文件最大 100MB（可配置）；容器基础镜像 Alpine  
**Scale/Scope**: 单用户/小团队私有部署，并发用户 <20

---

## Constitution Check

✅ **I. 单体架构分离**：所有业务逻辑新增于 `Services/`（`ILinkPreviewService`、`IThumbnailService`、`IBackgroundTaskQueue<T>`），Razor 组件仅通过 `@inject` 调用。  
✅ **II. 技术栈合规**：仅引入 `HtmlAgilityPack` + `SkiaSharp`（纯 .NET 库），不涉及 JS 框架或 CSS 组件库变更；Tailwind v4 继续使用。  
✅ **III. 命名规范**：新增方法均以 `Async` 结尾（`PinTopicAsync`、`SendMediaAsync`、`RetryUploadAsync` 等），接口 `I` 开头，PascalCase 全面覆盖。  
✅ **IV. 测试覆盖**：Service 层每个公开方法配套 xUnit 测试；SignalR `ShareItemUpdated` 广播使用 Moq 验证；E2E 至少覆盖"发送图片 → 预览 → 下载"完整链路。  
✅ **V. 安全合规**：魔数（Magic Bytes）校验文件真实 MIME；危险可执行扩展黑名单；下载端点含 `Content-Disposition: attachment`；无硬编码凭证；`HttpClient` 限 512KB 响应体防 OOM。  
✅ **VI. 容器化**：存储路径通过 `Storage:BasePath` 环境变量注入，文件目录通过 Docker Volume 挂载；SQLite DB 文件同 Volume。  
✅ **VII. RESTful API**：新增端点（`/api/v1/share-items/media`、`/api/v1/topics/{id}/pin` 等）全部使用 `app.Map*` Minimal API，组织于 `Api/ShareItemEndpoints.cs` / `Api/TopicEndpoints.cs`，不新建 Controller。

**Constitution Gate**: 通过 ✅，无需 Complexity Tracking 记录。

---

## Project Structure

### Documentation (this feature)

```text
specs/003-rich-media-chat/
├── plan.md              ← 本文件（/speckit.plan 输出）
├── spec.md              ← 功能规格（已完成）
├── research.md          ← Phase 0 研究汇总（已完成）
├── data-model.md        ← Phase 1 数据模型（已完成）
├── quickstart.md        ← Phase 1 开发者指南（已完成）
├── contracts/
│   └── api-contracts.md ← Phase 1 API+SignalR 契约（已完成）
├── checklists/
│   └── requirements.md  ← 质量核查清单（已完成）
└── tasks.md             ← Phase 2 输出（/speckit.tasks 命令，尚未生成）
```

### Source Code — 新增/变更文件清单

```text
AnyDrop/
├── Models/
│   ├── ShareItem.cs            [修改] 新增 UploadStatus / ThumbnailPath / OriginalFileName 字段
│   ├── Topic.cs                [修改] 新增 IsPinned / PinnedAt 字段
│   ├── ShareItemDto.cs         [修改] 新增 UploadStatus / ThumbnailPath / OriginalFileName /
│   │                                   LinkPreview / MediaMetadata DTO 字段
│   ├── TopicDto.cs             [修改] 新增 IsPinned / PinnedAt 字段
│   ├── UploadStatus.cs         [新增] UploadStatus 枚举（Completed / Uploading / Failed）
│   ├── LinkPreview.cs          [新增] LinkPreview 实体 + LinkPreviewDto
│   └── MediaMetadata.cs        [新增] MediaMetadata 实体 + MediaMetadataDto
│
├── Services/
│   ├── IShareService.cs        [修改] 新增 SendMediaAsync / SendAttachmentAsync / RetryUploadAsync
│   ├── ShareService.cs         [修改] 实现以上新方法
│   ├── ITopicService.cs        [修改] 新增 PinTopicAsync
│   ├── TopicService.cs         [修改] 实现 PinTopicAsync + 分页排序含置顶逻辑
│   ├── ILinkPreviewService.cs  [新增] 链接 meta 解析服务接口
│   ├── LinkPreviewService.cs   [新增] HtmlAgilityPack 实现，超时/大小保护
│   ├── IThumbnailService.cs    [新增] 缩略图生成服务接口
│   ├── ThumbnailService.cs     [新增] SkiaSharp（图片）+ FFmpeg CLI（视频）实现
│   ├── IBackgroundTaskQueue.cs [新增] 泛型后台队列接口（Channel<T> 实现）
│   └── BackgroundTaskQueue.cs  [新增] IHostedService 实现，消费缩略图/链接预览任务
│
├── Api/
│   ├── ShareItemEndpoints.cs   [修改] 新增 /media / /attachment / /{id}/retry /
│   │                                   /{id}/file / /{id}/thumbnail 端点
│   └── TopicEndpoints.cs       [修改] 新增 PUT /topics/{id}/pin 端点
│
├── Hubs/
│   └── ShareHub.cs             [修改] 新增 SendShareItemUpdatedAsync 方法
│
├── Data/
│   └── AnyDropDbContext.cs     [修改] 新增 DbSet<LinkPreview> / DbSet<MediaMetadata>；
│                                        新增索引；Topic 排序索引含 IsPinned
│
├── Migrations/
│   └── [新 Migration]          [新增] RichMediaChatEnhancements 迁移文件
│
├── Components/
│   ├── Pages/
│   │   └── Home.razor          [修改] 集成拖拽上传、Ctrl+Enter、懒加载、"回到最新"按钮
│   └── Layout/
│       ├── MediaPreviewModal.razor     [新增] 图片/视频预览 Modal
│       ├── AttachmentPreviewModal.razor [新增] 附件信息预览 Modal
│       ├── LinkPreviewCard.razor       [新增] 超链接卡片组件
│       ├── UploadProgressBubble.razor  [新增] 上传中状态气泡
│       └── NavMenu.razor / Sidebar     [修改] 主题置顶按钮 + 置顶置顶排序
│
└── wwwroot/
    └── js/
        └── dragdrop-interop.js  [新增] 拖拽文件 DataTransfer JS 桥接；
                                         IntersectionObserver；滚动到底部

AnyDrop.Tests.Unit/
├── Services/
│   ├── LinkPreviewServiceTests.cs    [新增]
│   ├── ThumbnailServiceTests.cs      [新增]
│   ├── ShareServiceTests.cs          [修改] 补充 SendMediaAsync / RetryUploadAsync 测试
│   └── TopicServiceTests.cs          [修改] 补充 PinTopicAsync + 排序逻辑测试
└── Api/
    └── ShareItemEndpointsTests.cs    [修改] 补充新端点测试

AnyDrop.Tests.E2E/
└── Tests/
    └── RichMediaTests.cs             [新增] 图片发送→预览→下载 E2E 链路测试
```

---

## 关键设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| 图片缩略图 | SkiaSharp | 跨平台，Alpine 支持，MIT 许可，无 GDI+ 依赖 |
| 视频预览帧 | FFmpeg CLI（进程调用） | 标准工具，不可用时降级，无原生 .NET 库与跨平台兼容 |
| HTML meta 解析 | HtmlAgilityPack | 成熟 MIT 库，轻量，本场景无需完整 DOM 解析 |
| 后台任务 | IHostedService + Channel\<T\> | .NET 内置，无额外队列基础设施，符合简洁原则 |
| 文件上传前端 | InputFile + ondrop（JS interop） | Blazor Server 原生 InputFile + 最小 JS 桥接 |
| 滚动检测 | IntersectionObserver（JS interop） | 浏览器原生 API，性能最优，无 Blazor 轮询 |
| 分页方式 | 基于游标（CreatedAt DESC） | 实时消息插入场景下 offset 会产生重复/漏数据 |
| 上传进度 | 两态（Uploading/Completed/Failed） | Blazor Server 无法原生字节级进度，两态已满足 UX 需求 |

---

## 风险与缓解

| 风险 | 缓解措施 |
|------|----------|
| FFmpeg 不在 Docker 镜像中 | 代码检测，不可用时静默降级（无缩略图，显示图标） |
| 链接预览抓取超时/失败 | 超时 5s，响应大小限 512KB，异常时静默降级 |
| 大文件阻塞上传线程 | Blazor Server 流式读取，MaxRequestBodySize 限制 |
| SkiaSharp Alpine 原生库缺失 | 添加 `SkiaSharp.NativeAssets.Linux` NuGet 包 |
| 恶意文件上传（类型伪造） | 魔数校验（文件头字节）+ 扩展名黑名单双重过滤 |
