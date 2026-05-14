# Feature 005 — 主题搜索

## 概述

为每个会话主题提供内置搜索功能，支持文字内容全文搜索、按日期定位消息、以及按媒体/文件/链接类型归类查看。

---

## 入口

- 位置：聊天主界面顶部标题栏，主题名右侧，主题设置图标左侧
- 控件：搜索图标按钮（`search`）
- 行为：点击后导航至 `/search/{topicId}`，进入当前主题的搜索页

---

## 路由

| 路由 | 页面 | 说明 |
|------|------|------|
| `/search/{topicId:guid}` | `TopicSearch.razor` | 主题搜索页 |

---

## 搜索页功能

### 标签页

搜索页顶部有六个标签页，可切换不同搜索/归类模式：

| 标签 | 图标 | 说明 |
|------|------|------|
| 文字 | `search` | 在当前主题所有消息中进行关键词全文搜索 |
| 日期 | `calendar_today` | 选择日期，显示该天全部消息 |
| 图片 | `image` | 列出主题内所有图片，支持网格预览 |
| 视频 | `videocam` | 列出主题内所有视频，支持内联播放 |
| 文件 | `description` | 列出主题内所有附件，可直接下载 |
| 链接 | `link` | 列出主题内所有链接，含标题/描述摘要 |

---

### 文字搜索

- 输入关键词，点击「搜索」按钮或按 Enter 触发搜索
- 搜索范围：消息 `Content` 字段（含文本、链接 URL 等）
- 匹配方式：大小写不敏感子串匹配（SQLite `LIKE '%q%'`）
- 结果按时间倒序排列，单次最多返回 50 条，支持「加载更多」
- 每条结果显示：内容摘要、消息时间；点击任意结果导航回聊天并高亮该消息

### 日期查找

- 提供日期选择器（最大可选值为今日）
- 点击「查看」后显示当天所有消息（使用服务器本地时区确定"一天"范围）
- 结果按时间升序排列，显示同日所有类型消息
- 点击结果可导航回聊天并高亮该消息

### 图片归类

- 自动加载主题内全部图片消息（按时间倒序，每页 50 张）
- 以网格（2 列，sm: 3 列）展示缩略图
- 鼠标悬浮显示操作条：在聊天中查看 / 下载
- 点击图片本体打开大图预览 Modal
- 大图预览支持关闭按钮与点击遮罩关闭

### 视频归类

- 列表形式展示主题内全部视频消息（按时间倒序，每页 50 条）
- 每条内嵌 `<video>` 播放器，支持直接播放
- 操作按钮：在聊天中查看 / 下载

### 文件归类

- 列表形式展示主题内全部附件消息（按时间倒序，每页 50 条）
- 显示文件图标、文件名、文件大小、发送时间
- 操作按钮：在聊天中查看 / 下载

### 链接归类

- 列表形式展示主题内全部链接消息（按时间倒序，每页 50 条）
- 显示链接标题、描述摘要（最多 2 行）、URL
- 操作按钮：在聊天中查看；链接 URL 可直接点击打开

---

## "在聊天中查看"交互

1. 点击任意搜索/归类结果的导航按钮
2. 浏览器导航至 `/?highlight={messageId}`
3. `Home.razor` 在初始化时读取 `highlight` 查询参数
4. 消息列表加载后，调用 `AnyDropInterop.scrollToMessage(messageId)` JS 函数
5. JS 通过 `[data-message-id]` 定位目标元素，执行平滑滚动并触发 `highlight-flash` 高亮动画（持续约 2.5 秒）

---

## API / 服务层

### `IShareService` 新增接口

```csharp
// 文字全文搜索（支持游标分页）
Task<TopicMessagesResponse> SearchTopicMessagesAsync(
    Guid topicId, string query,
    int limit = 50, DateTimeOffset? before = null,
    CancellationToken ct = default);

// 按日期查找（使用服务器本地时区）
Task<IReadOnlyList<ShareItemDto>> GetTopicMessagesByDateAsync(
    Guid topicId, DateOnly date,
    CancellationToken ct = default);

// 按内容类型查找（支持游标分页）
Task<TopicMessagesResponse> GetTopicMessagesByTypeAsync(
    Guid topicId, ShareContentType contentType,
    int limit = 50, DateTimeOffset? before = null,
    CancellationToken ct = default);
```

---

## 前端组件

| 文件 | 说明 |
|------|------|
| `Components/Pages/TopicSearch.razor` | 搜索页 Razor 模板，含六个标签页的完整 UI |
| `Components/Pages/TopicSearch.razor.cs` | 代码文件，含所有标签页的状态管理与服务调用 |

---

## JS 新增函数

```javascript
// dragdrop-interop.js
AnyDropInterop.scrollToMessage = function(messageId)
```

- 通过 `requestAnimationFrame` 等待 Blazor 渲染完成
- 使用 `[data-message-id]` 属性定位消息 DOM 元素
- 执行平滑滚动（`scrollIntoView({ behavior: 'smooth', block: 'center' })`）
- 添加/移除 `message-highlight` CSS 类触发高亮动画

---

## CSS 新增样式

```css
/* app.css */
@keyframes highlight-flash { ... }   /* 蓝色高亮闪烁 */
.message-highlight { ... }           /* 应用动画的类 */
```

---

## 已知限制

- 「在聊天中查看」只能定位到最近 50 条消息范围内的消息；若目标消息较旧（不在默认加载窗口内），滚动将静默失败（消息不在 DOM 中）。后续可通过按消息 ID 加载上下文来改善。
- 文字搜索仅匹配 `Content` 字段，不覆盖文件名或链接元数据字段。
- 日期范围基于服务器本地时区，若服务器与用户时区不同可能产生偏差。
