# Tasks: 主题会话侧边栏

**Feature**: `speckit/002-topic-session-sidebar`  
**Branch**: `speckit/002-topic-session-sidebar`  
**Date**: 2026-04-19  
**Constitution**: AnyDrop v2.0.0 — all tasks verified

---

## Phase 1: Setup（项目初始化）

- [X] T001 确认并切换到功能分支 `speckit/002-topic-session-sidebar`
- [X] T002 确认 .NET 10 SDK 已安装并执行 `dotnet restore`

---

## Phase 2: Foundational（基础先决任务，阻塞所有用户故事）

- [X] T003 新建 `AnyDrop/Models/Topic.cs`，定义 `Topic` 实体（`Id`、`Name`、`SortOrder`、`CreatedAt`、`LastMessageAt`）
- [X] T004 [P] 新建 `AnyDrop/Models/TopicDto.cs`，定义 `TopicDto`、`CreateTopicRequest`、`UpdateTopicRequest`、`ReorderTopicsRequest`、`TopicOrderItem`、`TopicMessagesResponse` 记录类
- [X] T005 在 `AnyDrop/Models/ShareItem.cs` 新增 `public Guid? TopicId { get; set; }` 字段（可空外键，向后兼容）
- [X] T006 更新 `AnyDrop/Data/AnyDropDbContext.cs`：新增 `public DbSet<Topic> Topics => Set<Topic>();`；在 `OnModelCreating` 中配置 `Topic` 实体（主键、MaxLength、索引 `(SortOrder, LastMessageAt)`、`CreatedAt` DateTimeOffset 转换）；为 `ShareItem` 添加 `HasOne<Topic>().WithMany().HasForeignKey(e => e.TopicId).OnDelete(DeleteBehavior.Restrict).IsRequired(false)` 及复合索引 `(TopicId, CreatedAt)`
- [X] T007 执行 `dotnet ef migrations add AddTopicAndRelations --project AnyDrop`，验证迁移文件生成正确后执行 `dotnet ef database update`
- [X] T008 新建 `AnyDrop/Services/ITopicService.cs`，声明接口方法：`GetAllTopicsAsync`、`CreateTopicAsync`、`UpdateTopicAsync`、`DeleteTopicAsync`、`ReorderTopicsAsync`、`GetTopicMessagesAsync`
- [X] T009 新建 `AnyDrop/Services/TopicService.cs`，实现 `ITopicService`，注入 `AnyDropDbContext` 和 `IHubContext<ShareHub>`；所有方法以 `Async` 结尾；实现排序逻辑 `ORDER BY SortOrder ASC, LastMessageAt DESC NULLS LAST, CreatedAt DESC`
- [X] T010 在 `AnyDrop/Program.cs` 注册 `builder.Services.AddScoped<ITopicService, TopicService>()`

---

## Phase 3: User Story 1 — 新建主题会话（P1）

**Story Goal**: 用户可在侧边栏新建主题，主题出现在侧边栏并成为当前选中主题，聊天区域显示空历史。  
**Independent Test**: 新建主题→侧边栏出现该主题→聊天区域为空，全流程可独立验收。

- [X] T011 [US1] 新建 `AnyDrop/Components/Layout/TopicSidebar.razor`：渲染主题列表（`@foreach topic in _topics`）、"+ 新建主题"按钮、空状态引导提示（无主题时显示）；Tailwind 工具类布局，无内联样式
- [X] T012 [US1] 新建 `AnyDrop/Components/Layout/TopicSidebar.razor.cs`（code-behind）：`@inject ITopicService`；`OnInitializedAsync` 加载 `_topics`；`CreateTopicAsync` 方法验证名称非空且 ≤100 字符，调用 `ITopicService.CreateTopicAsync`，刷新列表并设置 `_selectedTopicId`
- [X] T013 [P] [US1] 更新 `AnyDrop/Components/Layout/MainLayout.razor`：在左侧嵌入 `<TopicSidebar>` 组件，传入 `OnTopicSelected` 回调参数；布局使用 Tailwind flex/grid
- [X] T014 [P] [US1] 在 `AnyDrop/wwwroot/app.css` 添加 `@layer components` 中的侧边栏样式（`.sidebar-item`、`.sidebar-item--active`、`.sidebar-empty-state`）
- [X] T015 [US1] 新建 `AnyDrop/Api/TopicEndpoints.cs`，实现 `POST /api/v1/topics`（创建主题，返回 201）、`GET /api/v1/topics`（获取全部主题，返回 200）；注入 `ITopicService`；验证 `CreateTopicRequest.Name`；返回统一 `{ success, data, error }` JSON 结构
- [X] T016 [US1] 在 `AnyDrop/Program.cs` 调用 `app.MapTopicEndpoints()`（`TopicEndpoints.cs` 中实现 `MapTopicEndpoints` 扩展方法）

---

## Phase 4: User Story 2 — 切换主题查看历史（P1）

**Story Goal**: 点击侧边栏主题，聊天区域在 1 秒内展示该主题的完整（分页）历史消息，当前主题高亮显示。  
**Independent Test**: 两个主题各有不同消息，来回点击后聊天区域正确切换，可独立验收。

- [X] T017 [US2] 在 `TopicService.cs` 实现 `GetTopicMessagesAsync(Guid topicId, int limit, DateTimeOffset? before)`：游标分页查询，`WHERE TopicId = @id AND (@before IS NULL OR CreatedAt < @before) ORDER BY CreatedAt DESC LIMIT @limit`；返回 `TopicMessagesResponse`（含 `HasMore`、`NextCursor`）
- [X] T018 [US2] 在 `TopicEndpoints.cs` 新增 `GET /api/v1/topics/{id}/messages?limit=50&before=`：调用 `ITopicService.GetTopicMessagesAsync`；`id` 不存在时返回 404
- [X] T019 [US2] 在 `TopicSidebar.razor.cs` 新增 `SelectTopicAsync(Guid topicId)`：更新 `_selectedTopicId`、通知 `OnTopicSelected` 回调、触发 `StateHasChanged`；当前选中主题应用 `.sidebar-item--active` 样式
- [X] T020 [P] [US2] 更新 `AnyDrop/Components/Pages/Home.razor`（或对应聊天区域组件）：接收 `SelectedTopicId`、调用 `ITopicService.GetTopicMessagesAsync` 加载历史；无消息时显示空状态提示"暂无内容，发送第一条消息吧"；无主题时显示引导选择提示
- [X] T021 [P] [US2] 在 `TopicEndpoints.cs` 新增 `PUT /api/v1/topics/{id}`（更新名称）和 `DELETE /api/v1/topics/{id}`（删除主题，消息 `TopicId` 置 null）

---

## Phase 5: User Story 3 — 主题按最后消息日期实时排序（P2）

**Story Goal**: 侧边栏按 `LastMessageAt DESC` 自动排序，新消息发送后 1 秒内实时更新排序，无需刷新。  
**Independent Test**: 向非顶部主题发消息，侧边栏该主题实时上升至顶部，独立可验收。

- [X] T022 [US3] 更新 `AnyDrop/Hubs/ShareHub.cs`：新增 `SendTopicsUpdatedAsync(IReadOnlyList<TopicDto> topics)` 方法，通过 `Clients.All.SendAsync("TopicsUpdated", topics)` 广播
- [X] T023 [US3] 更新 `TopicService.cs` 中的 `CreateTopicAsync`、`DeleteTopicAsync`、`ReorderTopicsAsync`：操作完成后调用 `IHubContext<ShareHub>.Clients.All.SendAsync("TopicsUpdated", updatedTopics)` 广播最新主题列表
- [X] T024 [US3] 更新现有消息发送逻辑（`ShareService.cs` 中 `SendTextAsync` 等）：发送消息时若 `TopicId` 不为 null，更新对应 `Topic.LastMessageAt = DateTimeOffset.UtcNow`，然后广播 `TopicsUpdated`
- [X] T025 [US3] 更新 `TopicSidebar.razor.cs`：在 `OnInitializedAsync` 中订阅 SignalR `TopicsUpdated` 事件（`HubConnection.On<IReadOnlyList<TopicDto>>("TopicsUpdated", ...)`）；收到事件时更新 `_topics` 并调用 `InvokeAsync(StateHasChanged)`；在 `DisposeAsync` 中取消订阅

---

## Phase 6: User Story 4 — 拖拽自定义排序（P3）

**Story Goal**: 用户可拖拽侧边栏主题改变顺序，顺序持久化后刷新页面仍保持，覆盖自动排序。  
**Independent Test**: 拖拽主题到新位置 → 刷新页面 → 顺序保持，独立可验收。

- [X] T026 [US4] 在 `AnyDrop/wwwroot/js/` 新建 `sortable-interop.js`：引入 SortableJS（CDN URL: `https://cdn.jsdelivr.net/npm/sortablejs@1/Sortable.min.js`）；导出 `initSortable(elementId, dotnetRef)` 函数；在 `onEnd` 回调中收集新顺序并调用 `dotnetRef.invokeMethodAsync('OnSortEnd', newOrder)`
- [X] T027 [US4] 在 `AnyDrop/Components/App.razor`（或 `_Host.cshtml`）的 `<head>` 中添加 SortableJS CDN `<script>` 标签和 `<script src="/js/sortable-interop.js">` 引用
- [X] T028 [US4] 在 `TopicSidebar.razor.cs` 中：注入 `IJSRuntime`；`OnAfterRenderAsync(firstRender)` 时调用 `JS.InvokeVoidAsync("initSortable", "topic-list", DotNetObjectReference.Create(this))`；添加 `[JSInvokable] public async Task OnSortEnd(Guid[] orderedIds)` 方法，构建 `ReorderTopicsRequest` 并调用 `ITopicService.ReorderTopicsAsync`（乐观更新：先更新本地 `_topics` 顺序，失败时回滚）
- [X] T029 [US4] 在 `TopicService.cs` 实现 `ReorderTopicsAsync(ReorderTopicsRequest request)`：批量更新 `Topic.SortOrder` 字段；使用事务保证原子性；完成后广播 `TopicsUpdated`
- [X] T030 [US4] 在 `TopicEndpoints.cs` 新增 `PUT /api/v1/topics/reorder`：接收 `ReorderTopicsRequest`，验证 `items` 非空，调用 `ITopicService.ReorderTopicsAsync`
- [X] T031 [P] [US4] 在 `TopicSidebar.razor` 中为主题列表容器添加 `id="topic-list"` 属性，为每个主题条目添加 `data-id="@topic.Id"` 属性（供 SortableJS 读取顺序）

---

## Phase 7: Polish & Cross-Cutting Concerns（收尾与横切关注点）

- [X] T032 在 `TopicSidebar.razor.cs` 中 `IAsyncDisposable.DisposeAsync`：释放 `DotNetObjectReference`、调用 `JS.InvokeVoidAsync("destroySortable", "topic-list")`（需在 `sortable-interop.js` 中导出 `destroySortable`）、取消 SignalR 订阅
- [X] T033 [P] 在 `AnyDrop.Tests.Unit` 新建 `Services/TopicServiceTests.cs`：覆盖 `CreateTopicAsync_WhenNameIsEmpty_ThrowsException`、`CreateTopicAsync_WhenNameIsValid_ReturnsTopicDto`、`GetAllTopicsAsync_ReturnsTopicsInCorrectSortOrder`、`ReorderTopicsAsync_UpdatesSortOrderInDatabase`、`GetTopicMessagesAsync_WithCursor_ReturnsPaginatedResults`、`DeleteTopicAsync_SetsTopicIdNullOnMessages`
- [X] T034 [P] 验证 `TopicService.cs` 中所有调用 `IHubContext<ShareHub>` 的路径均有 Moq 验证（在 `TopicServiceTests.cs` 中 mock `IHubContext<ShareHub>` 并验证 `SendAsync("TopicsUpdated", ...)` 被调用）
- [X] T035 [P] 在 `AnyDrop.Tests.E2E` 新建 `TopicSidebarTests.cs`：至少一个 Playwright 用例验证"新建主题→在该主题下发消息→在第二个浏览器窗口观察侧边栏主题排序实时更新"完整链路（对应 SC-002）
- [X] T036 执行 `dotnet build` 确认无编译警告/错误；执行 `dotnet test` 确认所有单元测试通过

---

## Dependencies（用户故事完成顺序）

```
Phase 1 (Setup)
    ↓
Phase 2 (Foundational) — T003~T010 必须在所有用户故事之前完成
    ↓
Phase 3 (US1: 新建主题) ──→ Phase 4 (US2: 切换历史) ──→ Phase 5 (US3: 实时排序) ──→ Phase 6 (US4: 拖拽排序)
                                                                                               ↓
                                                                                        Phase 7 (Polish)
```

**故事间依赖**:
- US2 依赖 US1（需要侧边栏组件和 `TopicSidebar.razor.cs` 框架）
- US3 依赖 US1 + US2（需要 SignalR 广播逻辑和消息发送流程）
- US4 依赖 US1（需要 `TopicSidebar.razor.cs` 组件框架和 `ITopicService.ReorderTopicsAsync`）
- US4 **不**依赖 US3（可并行开发）

---

## Parallel Execution Examples

**US1 内部可并行**:
- T011（Razor 标记）可与 T012（code-behind 逻辑）同时进行
- T013（MainLayout 嵌入）、T014（CSS 样式）可与 T015（API 端点）同时进行

**US2 内部可并行**:
- T017（分页查询）可与 T020（首页组件更新）同时进行
- T021（PUT/DELETE 端点）可与 T019（切换逻辑）同时进行

**US4 内部可并行**:
- T026（JS 文件）、T027（script 引用）可与 T029（服务层）、T030（端点）同时进行
- T031（模板属性）可在 T026 完成前独立完成

**Phase 7**:
- T033、T034、T035 可完全并行（测试文件独立）

---

## Implementation Strategy

**MVP Scope（建议首先交付）**: Phase 2 + Phase 3 + Phase 4  
完成后用户即可新建主题、切换主题查看历史 — 核心价值交付完毕。

**Increment 2**: Phase 5（实时排序）  
**Increment 3**: Phase 6（拖拽排序）  
**Increment 4**: Phase 7（全量测试 + 收尾）

---

## Task Summary

| Phase | 任务数 | 用户故事 |
|-------|--------|---------|
| Phase 1: Setup | 2 | — |
| Phase 2: Foundational | 8 | — |
| Phase 3: US1 新建主题 | 6 | P1 |
| Phase 4: US2 切换历史 | 5 | P1 |
| Phase 5: US3 实时排序 | 4 | P2 |
| Phase 6: US4 拖拽排序 | 6 | P3 |
| Phase 7: Polish | 5 | — |
| **Total** | **36** | |

**可并行任务（标注 [P]）**: 12 个  
**用户故事独立测试入口**: US1 (T011–T016)、US2 (T017–T021)、US3 (T022–T025)、US4 (T026–T031)
