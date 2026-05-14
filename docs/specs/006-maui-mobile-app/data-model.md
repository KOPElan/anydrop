# Data Model: AnyDrop 移动端 App（MAUI Blazor Hybrid）

**Feature**: `006-maui-mobile-app`  
**Phase**: 1 — Design & Contracts  
**Date**: 2026-04-29

> 客户端 DTO 完全镜像服务端 `AnyDrop/Models/` 中的对应类型，以保证 JSON 反序列化兼容性。
> 客户端不包含 EF Core 实体（无本地数据库），仅定义纯数据载体（`record` 类型）。

---

## 1. 认证与会话

### AuthModels.cs

```csharp
namespace AnyDrop.App.Models;

// ── 服务端 setup-status 响应 ──────────────────────────────────────────────
public sealed record SetupStatusDto(bool RequiresSetup);

// ── 首次初始化（创建账号）请求 ───────────────────────────────────────────
public sealed record SetupRequest(
    string Nickname,
    string Password,
    string ConfirmPassword
);

// ── 登录请求 ─────────────────────────────────────────────────────────────
public sealed record LoginRequest(string Password);

// ── 登录响应（包含 JWT） ──────────────────────────────────────────────────
public sealed record LoginResponse(
    UserProfileDto User,
    string AccessToken,
    DateTimeOffset ExpiresAt
);

// ── 当前用户信息 ──────────────────────────────────────────────────────────
public sealed record UserProfileDto(
    string Nickname,
    DateTimeOffset? LastLoginAt
);
```

**本地状态（内存，Singleton Service 管理）**:

| 字段 | 存储位置 | 说明 |
|------|---------|------|
| `AccessToken` | `SecureStorage["anydrop_jwt"]` | JWT Bearer Token |
| `ExpiresAt` | `SecureStorage["anydrop_jwt_expiry"]` | 过期时间（Ticks 字符串） |
| `Nickname` | `Preferences["anydrop_nickname"]` | 当前用户昵称（非敏感） |
| `BaseUrl` | `Preferences["anydrop_base_url"]` | 服务端 URL |
| `Language` | `Preferences["anydrop_language"]` | `zh-CN` / `en` |
| `Theme` | `Preferences["anydrop_theme"]` | `light` / `dark` |

---

## 2. 主题（Topic）

### TopicModels.cs

```csharp
namespace AnyDrop.App.Models;

// ── 主题完整 DTO（服务端返回） ────────────────────────────────────────────
public sealed record TopicDto(
    Guid Id,
    string Name,
    string Icon,                       // emoji 或图标标识符
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastMessageAt,
    int MessageCount,
    bool IsBuiltIn,                    // 内置主题（不可删除）
    string? LastMessagePreview,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    DateTimeOffset? ArchivedAt
);

// ── 创建主题 ──────────────────────────────────────────────────────────────
public sealed record CreateTopicRequest(string Name);

// ── 更新主题名称 ──────────────────────────────────────────────────────────
public sealed record UpdateTopicRequest(string Name);

// ── 更新主题图标 ──────────────────────────────────────────────────────────
public sealed record UpdateTopicIconRequest(string Icon);

// ── 置顶/取消置顶 ─────────────────────────────────────────────────────────
public sealed record PinTopicRequest(bool IsPinned);

// ── 归档/取消归档 ─────────────────────────────────────────────────────────
public sealed record ArchiveTopicRequest(bool IsArchived);

// ── 批量重新排序 ──────────────────────────────────────────────────────────
public sealed record ReorderTopicsRequest(IReadOnlyList<TopicOrderItem> Items);

public sealed record TopicOrderItem(Guid TopicId, int SortOrder);

// ── 消息分页响应 ──────────────────────────────────────────────────────────
public sealed record TopicMessagesResponse(
    IReadOnlyList<ShareItemDto> Messages,
    bool HasMore,
    string? NextCursor                 // before 游标（ISO 8601 时间戳）
);
```

**状态转换**（FR-010/013）:

```
active ──[pin]──→ active+pinned
active ──[archive]──→ archived
archived ──[unarchive]──→ active
active/archived ──[delete]──→ ✕ (deleted)
```

---

## 3. 消息（ShareItem）

### ShareItemModels.cs

```csharp
namespace AnyDrop.App.Models;

// ── 内容类型枚举 ──────────────────────────────────────────────────────────
public enum ShareContentType
{
    Text  = 0,
    File  = 1,
    Image = 2,
    Video = 3,
    Link  = 4
}

// ── 消息 DTO（服务端返回） ────────────────────────────────────────────────
public sealed record ShareItemDto(
    Guid Id,
    ShareContentType ContentType,
    string Content,                    // Text: 文字内容；File/Image/Video: 文件访问路径；Link: URL
    string? FileName,
    long? FileSize,                    // 字节数
    string? MimeType,
    string? LinkTitle,
    string? LinkDescription,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,         // null = 永不过期
    Guid? TopicId
);

// ── 发送文本消息 ──────────────────────────────────────────────────────────
public sealed record CreateTextShareItemRequest(
    string Content,
    Guid? TopicId                      // null → 默认主题
);

// ── 搜索结果（复用 ShareItemDto） ─────────────────────────────────────────
// GET /api/v1/topics/{id}/messages/search → IReadOnlyList<ShareItemDto>

// ── 按日期查询响应（复用 ShareItemDto） ───────────────────────────────────
// GET /api/v1/topics/{id}/messages/by-date → IReadOnlyList<ShareItemDto>

// ── 活跃日期响应 ──────────────────────────────────────────────────────────
public sealed record ActiveDatesResponse(IReadOnlyList<DateOnly> Dates);
```

**消息渲染策略**:

| ContentType | 渲染组件 | 点击行为 |
|------------|---------|---------|
| `Text` | `<pre>`/段落 | 无 |
| `Image` | `<img>` 缩略图 | 打开 `ImagePreview.razor` 全屏 |
| `Video` | 缩略图 + 播放图标 | 打开原生视频播放器（`Launcher.OpenAsync`） |
| `File` | 文件图标 + 文件名/大小 | 下载到设备 |
| `Link` | OGP 卡片（标题 + 描述） | 打开系统浏览器 |

**文件 URL 构造规则**:
```
GET {BaseUrl}/api/v1/share-items/{id}/file           → 预览（图片内嵌）
GET {BaseUrl}/api/v1/share-items/{id}/file?download=true → 强制下载
```

---

## 4. 设置

### SettingsModels.cs

```csharp
namespace AnyDrop.App.Models;

// ── 安全设置 DTO ──────────────────────────────────────────────────────────
public sealed record SecuritySettingsDto(
    bool AutoFetchLinkPreview,
    int BurnAfterReadingMinutes,       // 0 = 不启用阅后即焚
    string Language,                   // "zh-CN" | "en"
    bool AutoCleanupEnabled,
    int AutoCleanupMonths              // 1 | 3 | 6
);

// ── 更新安全设置 ──────────────────────────────────────────────────────────
public sealed record UpdateSecuritySettingsRequest(
    bool AutoFetchLinkPreview,
    int BurnAfterReadingMinutes,
    string Language,
    bool AutoCleanupEnabled,
    int AutoCleanupMonths
);

// ── 更新昵称 ──────────────────────────────────────────────────────────────
public sealed record UpdateNicknameRequest(string Nickname);

// ── 修改密码 ──────────────────────────────────────────────────────────────
public sealed record UpdatePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword
);
```

---

## 5. 跨层本地状态模型

### AppState（Scoped Service）

以下字段存在于 `IAppStateService`（Scoped，每个 BlazorWebView 渲染周期一个实例）：

```csharp
public interface IAppStateService
{
    // 当前选中主题 ID
    Guid? CurrentTopicId { get; set; }
    
    // 已加载的主题列表（内存缓存）
    IReadOnlyList<TopicDto> Topics { get; set; }
    
    // 当前主题的消息列表（会话内缓存）
    List<ShareItemDto> Messages { get; set; }
    
    // 是否还有更多历史消息可加载
    bool HasMoreMessages { get; set; }
    
    // 分页游标（before 参数）
    string? MessageCursor { get; set; }
    
    // SignalR 连接状态
    HubConnectionState SignalRState { get; set; }
    
    // 事件：状态变更通知 Blazor 组件重新渲染
    event Action? OnChange;
    void NotifyStateChanged();
}
```

### SharedContent（Platform 跨层数据）

```csharp
// 跨平台分享数据（从 Android/iOS 原生层传入 Blazor）
public sealed record SharedContent(
    string? Text,
    IReadOnlyList<string> FilePaths,   // App cache 中的临时文件路径
    string? MimeType
);
```

---

## 6. API 通用响应封装

服务端所有 API 响应遵循统一 JSON 结构（参考 `ApiEnvelope.cs`）：

```json
{
  "success": true,
  "data": { ... },
  "error": null
}
```

客户端解析辅助类型：

```csharp
namespace AnyDrop.App.Models;

public sealed record ApiResponse<T>(bool Success, T? Data, string? Error);
```

---

## 实体关系图

```
ServerConfig (Preferences)
    └── BaseUrl → 用于构造所有 API URL

AuthSession (SecureStorage)
    ├── AccessToken (JWT)
    ├── ExpiresAt
    └── Nickname

Topic (1) ──── (N) ShareItem
    ├── IsPinned
    ├── IsArchived
    └── SortOrder

ShareItem
    ├── ContentType: Text | File | Image | Video | Link
    ├── Content: text内容 or 文件路径 or URL
    └── ExpiresAt: 阅后即焚到期时间

SecuritySettings (服务端持久化，App 从 /api/v1/settings/security 获取)
    ├── BurnAfterReadingMinutes
    ├── AutoCleanupEnabled / AutoCleanupMonths
    ├── AutoFetchLinkPreview
    └── Language
```
