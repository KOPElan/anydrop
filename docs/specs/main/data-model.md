# Data Model: 核心基础设施与最小 MVP

**Phase**: 1 — Design  
**Date**: 2026-04-18  
**Feature**: feat/001-core-infra-mvp

---

## 实体总览

| 实体 / 类型 | 类别 | 说明 |
|---|---|---|
| `ShareContentType` | Enum | 内容类型枚举 |
| `ShareItem` | EF Core 实体 | 持久化核心数据 |
| `ShareItemDto` | DTO | SignalR 广播 / API 响应用 |
| `AnyDropDbContext` | DbContext | EF Core 数据库上下文 |

---

## ShareContentType 枚举

**命名空间**: `AnyDrop.Models`  
**文件**: `Models/ShareContentType.cs`

```csharp
namespace AnyDrop.Models;

public enum ShareContentType
{
    Text  = 0,   // 纯文本
    File  = 1,   // 通用文件（二进制）
    Image = 2,   // 图片
    Video = 3,   // 视频
    Link  = 4    // 网页链接 / URL
}
```

**设计说明**:
- 显式赋值整数值，保证 Migration 数据库字段值稳定（日后枚举顺序调整不影响已有数据）
- MVP 阶段仅使用 `Text`；其余值预先保留，实体结构无需修改即可支持后续类型

---

## ShareItem 实体

**命名空间**: `AnyDrop.Models`  
**文件**: `Models/ShareItem.cs`

```csharp
namespace AnyDrop.Models;

public sealed class ShareItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ShareContentType ContentType { get; set; } = ShareContentType.Text;

    // 文本类型：纯文本内容；Link 类型：URL；File/Image/Video：存储路径
    public string Content { get; set; } = string.Empty;

    // 仅文件类型使用：原始文件名（含扩展名）
    public string? FileName { get; set; }

    // 仅文件类型使用：文件大小（字节）
    public long? FileSize { get; set; }

    // 仅文件类型使用：MIME 类型（如 image/png）
    public string? MimeType { get; set; }

    // 服务端接收时间（UTC）
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // 转换为 DTO 的辅助方法（保持 DTO 创建逻辑集中）
    public ShareItemDto ToDto() => new(Id, ContentType, Content, FileName, FileSize, MimeType, CreatedAt);
}
```

**字段说明**:

| 字段 | 类型 | 可空 | 说明 |
|---|---|---|---|
| `Id` | `Guid` | ❌ | 主键，新建时自动生成 |
| `ContentType` | `ShareContentType` | ❌ | 内容类型枚举，EF Core 存储为整数 |
| `Content` | `string` | ❌ | 文本/URL/文件存储路径，Text 类型最大 10,000 字符 |
| `FileName` | `string?` | ✅ | 文件原始名称，仅 File/Image/Video 使用 |
| `FileSize` | `long?` | ✅ | 字节数，仅文件类型使用 |
| `MimeType` | `string?` | ✅ | MIME 类型，仅文件类型使用，用于后续验证 |
| `CreatedAt` | `DateTimeOffset` | ❌ | 服务端 UTC 时间，排序依据 |

**验证规则**:
- `Content` 不可为空，Text 类型长度 1–10,000 字符（服务层验证）
- `ContentType` 为 Text 时，`FileName`/`FileSize`/`MimeType` 应为 `null`
- `Id` 由服务端生成，客户端不可指定

**状态转换**:
- ShareItem 一旦创建即为终态（不可变）；MVP 阶段不支持编辑或撤回

---

## ShareItemDto 传输对象

**命名空间**: `AnyDrop.Models`  
**文件**: `Models/ShareItemDto.cs`

```csharp
namespace AnyDrop.Models;

// 使用 record 确保不可变、自动 Equals/GetHashCode、JSON 序列化友好
public sealed record ShareItemDto(
    Guid Id,
    ShareContentType ContentType,
    string Content,
    string? FileName,
    long? FileSize,
    string? MimeType,
    DateTimeOffset CreatedAt
);
```

**用途**:
- SignalR 广播：`hub.Clients.All.SendAsync("ReceiveShareItem", dto)`
- Minimal API 响应体：`GET /api/v1/share-items`、`POST /api/v1/share-items`
- Blazor 组件绑定：`List<ShareItemDto>` 驱动消息列表渲染

**与实体的区别**:
- 不包含 EF Core 导航属性（当前无关联实体）
- 为 `record` 类型，值相等语义，线程安全
- 可直接 JSON 序列化（System.Text.Json 内置支持）

---

## AnyDropDbContext

**命名空间**: `AnyDrop.Data`  
**文件**: `Data/AnyDropDbContext.cs`

```csharp
using AnyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Data;

public sealed class AnyDropDbContext(DbContextOptions<AnyDropDbContext> options) : DbContext(options)
{
    public DbSet<ShareItem> ShareItems => Set<ShareItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShareItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ContentType).HasConversion<int>();
            entity.Property(e => e.Content).HasMaxLength(10_000).IsRequired();
            entity.Property(e => e.FileName).HasMaxLength(260);
            entity.Property(e => e.MimeType).HasMaxLength(127);
            entity.HasIndex(e => e.CreatedAt);  // 支持按时间排序查询
        });
    }
}
```

**设计决策**:
- `HasConversion<int>()` 将枚举存储为整数，避免枚举名称变更导致的数据库迁移
- `HasIndex(e => e.CreatedAt)` 支持 `GetRecentAsync` 的 `ORDER BY CreatedAt DESC LIMIT N` 高效查询
- 构造函数使用 Primary Constructor 语法（C# 12+），简洁且与 EF Core DI 模式兼容

---

## 服务接口定义

### IShareService

**文件**: `Services/IShareService.cs`

```csharp
using AnyDrop.Models;

namespace AnyDrop.Services;

public interface IShareService
{
    Task<ShareItemDto> SendTextAsync(string content, CancellationToken ct = default);
    Task<IReadOnlyList<ShareItemDto>> GetRecentAsync(int count = 50, CancellationToken ct = default);
}
```

### IFileStorageService

**文件**: `Services/IFileStorageService.cs`

```csharp
namespace AnyDrop.Services;

public interface IFileStorageService
{
    Task<string> SaveFileAsync(Stream content, string fileName, string mimeType, CancellationToken ct = default);
    Task<Stream> GetFileAsync(string storagePath, CancellationToken ct = default);
    Task DeleteFileAsync(string storagePath, CancellationToken ct = default);
}
```

---

## 数据流图

```text
用户在 Home.razor 输入文本并点击发送
        │
        ▼
IShareService.SendTextAsync(content)
        │
        ├─── 1. 创建 ShareItem 实体（服务端时间戳）
        ├─── 2. db.ShareItems.Add(item)
        ├─── 3. await db.SaveChangesAsync()      ──► SQLite (anydrop.db)
        ├─── 4. item.ToDto() → ShareItemDto
        └─── 5. hub.Clients.All.SendAsync("ReceiveShareItem", dto)
                        │
                        ▼
        所有已连接的 Blazor Server 客户端
        接收 "ReceiveShareItem" 事件，更新 UI
```