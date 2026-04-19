# Research: 主题会话侧边栏

**Feature**: `speckit/002-topic-session-sidebar`  
**Date**: 2026-04-19  
**Status**: Complete — all NEEDS CLARIFICATION resolved

---

## 1. Blazor Server 中侧边栏主题列表的实时更新

**Decision**: 使用 SignalR Hub 向所有已连接客户端广播 `TopicsUpdated` 消息，侧边栏 Razor 组件通过 `HubConnection`（或 `IHubContext<ShareHub>` 服务端广播）接收并刷新列表。

**Rationale**: 现有 `ShareHub` 已是 Blazor Server 架构中 SignalR 的入口点，向其添加 `TopicsUpdated` 方法符合现有模式，无需引入额外依赖。Blazor Server 自带 SignalR 连接，可直接复用。

**Alternatives considered**:
- Blazor `EventCallback`/Cascading State: 仅限于同一会话树，跨标签页/设备无效，已排除。
- Server-Sent Events: 不符合已有 SignalR 基础设施，已排除。

---

## 2. 拖拽排序（P3）实现方案

**Decision**: 使用 [SortableJS](https://sortablejs.github.io/Sortable/)（MIT 协议，<15KB gzip）通过 Blazor JS Interop 调用。SortableJS 通过 `<script>` CDN 引入，拖拽结束回调 `onEnd` 时通过 `DotNetObjectReference` 调用 Blazor 侧的 C# 方法更新排序并持久化到数据库。

**Rationale**:
- HTML5 原生 DnD API 在移动端支持差，且列表重排序的视觉反馈需要大量手动代码，成本高。
- SortableJS 轻量、无框架依赖，可纯粹通过 JS Interop 集成，不违反 Tailwind-only 的 UI 规范。
- 无需 npm 构建链，可直接 CDN 引入（或放至 `wwwroot/lib/`）。

**Alternatives considered**:
- `@blazor-dragdrop`（NuGet 包）：已停止维护，已排除。
- 自行实现 HTML5 DnD：代码量大、跨浏览器坑多，已排除。

**Implementation note**: 排序权重存储于 `Topic.SortOrder`（int），前端拖拽后将新顺序通过 API 端点 `PUT /api/v1/topics/reorder` 批量更新；乐观更新在本地先行渲染，若服务端返回错误则回滚。

---

## 3. 历史消息分页 / 懒加载

**Decision**: 采用基于游标（cursor-based）分页：初始加载最新 50 条消息，用户滚动到顶部时触发加载更早的消息，游标为最早已加载消息的 `CreatedAt`。

**Rationale**:
- 相较于 offset 分页，游标分页在消息实时插入场景下不会出现消息重复或跳过的问题。
- Blazor Server 可通过 JS Interop 监听 Intersection Observer 触发"加载更多"。
- 50 条初始加载满足 SC-003（1 秒内展示），且绝大多数会话消息量不会超过此数。

**Alternatives considered**:
- `Virtualize<T>` Blazor 组件：适合已知总数量的列表，聊天消息是增量追加场景，且需要顶部无限滚动，`Virtualize` 仅原生支持向下滚动，需大量定制，已排除。
- 一次性加载全部消息：违反 FR-010，已排除。

---

## 4. Topic 实体与 ShareItem 外键关联

**Decision**: 新增 `Topic` EF Core 实体，`ShareItem` 添加可空外键 `TopicId`（`Guid?`）。可空是为了向后兼容现有无主题的消息数据。

**Rationale**:
- 现有 `ShareItem` 记录无主题归属，迁移时保持 `TopicId = null` 表示"未分类"，不破坏现有功能。
- 通过 EF Core `HasOne`/`WithMany` 配置级联策略为 `Restrict`（防止误删主题导致消息丢失）。

**Alternatives considered**:
- 独立 MessageTopic 关联表：过度设计，一条消息属于一个主题，一对多即可，已排除。

---

## 5. 手动排序与自动排序的共存策略

**Decision**: `Topic.SortOrder` 字段存储用户自定义排序权重（整数，越小越靠前）。默认值为 `int.MaxValue`（未手动排序）。侧边栏排序规则：**先按 `SortOrder` 升序排，`SortOrder` 相同时按 `LastMessageAt` 降序排**。新建主题默认 `SortOrder = int.MaxValue`，自动跟随"按时间"排序逻辑排列在同权重组的顶部。

**Rationale**:
- 用户如从未拖拽，所有主题 `SortOrder = int.MaxValue`，退化为纯按时间排序，满足 FR-004。
- 用户拖拽后，只影响被拖拽的主题，其他主题不受影响。
- 实现简单，无需复杂的排序算法（分数法类似 Trello）。

**Alternatives considered**:
- 浮点数分数法（LexoRank）：防止频繁重排时整数用尽，但本场景主题数量小（<100），整数足够；可在后续迭代升级，已推迟。

---

## 6. EF Core SQLite 迁移策略

**Decision**: 生成新的 EF Core Migration（`AddTopicAndRelations`），通过 `DatabaseMigrationExtensions` 在应用启动时自动 `MigrateAsync()`。

**Rationale**: 现有代码已有 `DatabaseMigrationExtensions`，直接复用自动迁移机制，无需手动 SQL。

---

## 7. 主题 API 端点设计

**Decision**: 在 `Api/TopicEndpoints.cs` 新增以下 Minimal API 端点，通过 `app.MapTopicEndpoints()` 在 `Program.cs` 注册：

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/v1/topics` | 获取所有主题（含排序） |
| `POST` | `/api/v1/topics` | 新建主题 |
| `PUT` | `/api/v1/topics/{id}` | 更新主题名称 |
| `DELETE` | `/api/v1/topics/{id}` | 删除主题 |
| `PUT` | `/api/v1/topics/reorder` | 批量更新排序 |
| `GET` | `/api/v1/topics/{id}/messages` | 获取主题消息（分页） |

**Rationale**: 遵循 Constitution VII，与现有 `ShareEndpoints.cs` 结构保持一致。

---

## 所有 NEEDS CLARIFICATION 已解决

| 原始疑问 | 解决方案 |
|----------|---------|
| 拖拽排序库选择 | SortableJS via CDN + JS Interop |
| 分页策略 | 游标分页（初始50条，顶部滚动触发加载） |
| 手动/自动排序共存 | SortOrder 字段，int.MaxValue 默认值 |
| 历史消息与主题关联 | ShareItem 添加可空 TopicId FK |
| 实时更新机制 | SignalR `TopicsUpdated` 广播 |
