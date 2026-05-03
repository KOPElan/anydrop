# AnyDrop App — 移动端设计规范

## 1. 设计理念：移动优先 · 轻量 · 沉浸

App 端借鉴 `UI/DESIGN.md` 的 **"Atmospheric Precision"** 理念，但针对移动端触控交互做了适配：
- **单手操作优先**：核心交互（发送消息、切换主题）集中在屏幕底部 1/3 区域
- **大间距大圆角**：`rounded-2xl`（1rem）起，营造亲和触感
- **无分割线原则**：用背景色过渡代替 `border` 分割（同 Web 端）
- **安全区域适配**：全面支持 iOS 刘海/Home Indicator 和 Android 状态栏

---

## 2. 配色方案

基于 Web 端的 Cool Gray + Vibrant Digital Cobalt，针对移动端提高对比度：

| Token | 浅色 | 深色 | 用途 |
|-------|------|------|------|
| `surface` | `#f5f7fa` | `#111827` (gray-900) | 页面背景 |
| `surface_container` | `#ffffff` | `#1f2937` (gray-800) | 卡片/输入区背景 |
| `primary` | `#4f46e5` (indigo-600) | `#818cf8` (indigo-400) | 主按钮/高亮 |
| `primary_gradient` | `linear-gradient(135deg, #4f46e5, #6366f1)` | 同上 | 主按钮渐变 |
| `on_surface` | `#1f2937` (gray-800) | `#f3f4f6` (gray-100) | 正文 |
| `on_surface_muted` | `#9ca3af` (gray-400) | `#6b7280` (gray-500) | 辅助文字 |
| `danger` | `#ef4444` (red-500) | `#f87171` (red-400) | 危险操作 |

---

## 3. 排版

- **界面文字**：系统字体栈（`-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto`）
- **层级**：
  - 页面标题：`text-lg font-semibold`
  - 内容/消息：`text-sm`
  - 时间戳/元数据：`text-xs text-gray-400`

---

## 4. 圆角与间距

| 层级 | 值 | 用途 |
|------|-----|------|
| `sm` | `0.5rem` | 小标签、内嵌元素 |
| `md` | `0.75rem` | 卡片内边距、小按钮 |
| `lg` | `1rem` (rounded-xl) | 输入框、中等卡片 |
| `xl` | `1.25rem` (rounded-2xl) | 大卡片、section 容器 |

---

## 5. 组件样式

### 按钮
- **主按钮**：`w-full py-3 bg-gradient-to-r from-indigo-600 to-indigo-500 text-white rounded-xl font-medium`
- **危险按钮**：`border border-red-500 text-red-500 rounded-xl`
- **图标按钮**：`w-10 h-10 rounded-full flex items-center justify-center`

### 输入框
- **圆角输入**：`w-full px-4 py-3 rounded-xl bg-white dark:bg-gray-800 text-sm`
- **聚焦态**：`ring-2 ring-indigo-500/30 ring-offset-0`

### 消息气泡
- **文本**：白色卡片 `rounded-2xl` 带弱阴影
- **链接**：浅蓝背景 `bg-blue-50 dark:bg-blue-900/20`
- **文件**：`rounded-xl` 带图标 + 文件名

### 底部导航栏
- `h-14` 固定高度，`safe-area-bottom` 适配 Home Indicator
- 图标 + 标签，激活态为 `text-indigo-600`

### 状态横幅
- 顶部横幅：`px-4 py-1.5 text-xs text-center`，圆角 `rounded-b-lg`

---

## 6. 动效

- 页面切换：无动画（Blazor 原生路由）
- 按钮点击：`active:scale-95 transition-transform duration-150`
- 列表项：`transition-colors duration-150`
- 弹窗/侧栏：`transition-transform duration-300`

---

## 7. 安全区域

所有全屏页面必须：
```html
<div class="safe-area-top"></div>  <!-- iOS 状态栏 -->
<div class="flex-1 overflow-y-auto">...</div>
<nav class="safe-area-bottom">...</nav>  <!-- iOS Home Indicator -->
```

---

## 8. 暗色模式

- 通过 Tailwind `dark:` 前缀控制
- 切换方式：`ThemeManager.setTheme('dark'|'light')` JS 互操作
- 默认跟随系统（`prefers-color-scheme`）
