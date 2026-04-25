# AnyDrop — 高保真 UI 设计规范 (design.md)

> 本文档基于项目高保真设计稿，定义 AnyDrop Web 应用的设计语言、组件规范和视觉标准。

---

## 1. 整体布局

```
┌────────────────────────────────────────────────────────────┐
│  Sidebar (220px)        │  Chat Main Area (flex-1)         │
│  ──────────────────     │  ──────────────────────────────  │
│  Brand Header           │  ▲ Frosted Glass Header          │
│    AnyDrop logo + [+]   │    Topic name · Search · Config  │
│  ─────────────────────  │                                  │
│  Sessions ─────────     │  Message List (scrollable)       │
│    • Default   23:14    │    [blue pill bubble]            │
│      unread             │    🕐 2026/04/19 18:26           │
│    • Photos    18:26    │                                  │
│      active ◀ selected  │                                  │
│  ─────────────────────  │                                  │
│  Devices ──────────     │  ▼ Frosted Glass Footer          │
│    💻 Current Device    │    [📷][📎][⏱] input… [→]       │
│       Online (green)    │                                  │
│    📱 Phone             │                                  │
│       Offline (gray)    │                                  │
│  ─────────────────────  │                                  │
│  [Settings]             │                                  │
│  [Logout]               │                                  │
└────────────────────────────────────────────────────────────┘
```

---

## 2. 颜色系统 (Color System)

| Token | Value | 用途 |
|-------|-------|------|
| `--color-primary` | `#0050cb` | 主按钮、链接、激活状态、消息气泡背景 |
| `--color-primary-container` | `#0066ff` | 渐变结束色、品牌图标 |
| `--color-primary-fixed` | `#dae1ff` | 激活侧边栏项背景 |
| `--color-on-primary` | `#ffffff` | 主色背景上的文字 |
| `--color-surface` | `#f8f9fa` | 页面/聊天区背景 |
| `--color-surface-container-low` | `#f3f4f5` | 侧边栏背景 |
| `--color-surface-container-lowest` | `#ffffff` | 卡片/气泡背景（非文本气泡） |
| `--color-on-surface` | `#191c1d` | 主文本色 |
| `--color-on-surface-variant` | `#424656` | 次级文字、图标 |
| `--color-outline` | `#727687` | 辅助文字、时间戳、分割线 |
| `--color-outline-variant` | `#c2c6d8` | 边框 |
| `--color-error` | `#ba1a1a` | 错误提示 |
| `online-green` | `#22c55e` | 设备在线指示点 |

---

## 3. 字体规范 (Typography)

| 级别 | Font | Size | Weight | 用途 |
|------|------|------|--------|------|
| Headline | Manrope | 16px / 18px | 800 ExtraBold | 品牌名、对话标题 |
| Body | Inter | 14px | 400 Regular | 消息正文、输入框 |
| Label | Inter | 12px | 600 SemiBold | 按钮标签、Section 标题 |
| Caption | Inter | 11px / 10px | 400–700 | 时间戳、状态标签 |

---

## 4. 侧边栏 (Sidebar)

### 4.1 品牌栏 (Brand Header)
- 高度：~64px（含上下 padding）
- 左侧：蓝色渐变圆角图标（`sync_alt` 或 `chat` icon）+ "AnyDrop" 文字
- 右侧：`[+]` 新建主题按钮（正方形圆角，hover 背景浅灰）

### 4.2 Section 标签
- 字号：10px，`font-bold uppercase tracking-widest`
- 颜色：`--color-outline`

### 4.3 会话主题项 (Session Item)
```
┌─ [icon] ─ [name] ─────────── [HH:mm] ─┐
│           [status badge]               │
└────────────────────────────────────────┘
```
- **激活状态 (active)**：
  - 背景：`--color-primary-fixed` (`#dae1ff`)
  - 文字：`--color-primary`，600 weight
  - 左侧 4px 蓝色竖条指示条（pill，带弹入动画）
  - 状态徽标：`active`（蓝色小标签）
- **普通状态 (inactive)**：
  - hover 背景：`--color-surface-container`
  - hover 轻微右移 2px
- **有未读消息 (unread)**：
  - 状态徽标：`unread`（蓝/橙色小标签，文字加粗）

### 4.4 设备列表 (Devices)
- 在线：绿色指示点 (`#22c55e`)，"Online" 标签
- 离线：灰色指示点，"Offline" 标签，透明度 50%

### 4.5 底部按钮
- 设置 + 登出，行高 40px，hover 背景 `--color-surface-container`
- 登出按钮文字颜色：`--color-error`（红色）

---

## 5. 聊天主区域 (Chat Main)

### 5.1 顶部标题栏 (Header Glass)
- 磨砂玻璃效果：`backdrop-filter: blur(20px) saturate(180%)`
- 背景：`rgba(248, 249, 250, 0.75)`
- 底部边框：`rgba(194, 198, 216, 0.35)`
- 内容：话题名（粗体）| 搜索图标 + 设置图标

### 5.2 消息气泡 (Message Bubbles)

#### 文本消息
- 样式：蓝色 pill 气泡
- 背景：`linear-gradient(135deg, #0050cb, #0066ff)`
- 文字：白色，14px
- 圆角：`rounded-b-2xl rounded-tr-2xl`（左上尖角表示消息方向）
- 阴影：`0 4px 16px rgba(0, 80, 203, 0.25)`
- hover：上移 2px + 加深阴影

#### 链接消息
- 白色卡片，带链接图标和打开按钮

#### 文件/图片/视频消息
- 白色卡片，带文件图标和下载按钮

### 5.3 时间戳
- 气泡下方，左对齐
- 颜色：`--color-primary`（蓝色），12px
- 格式：`🕐 yyyy/MM/dd HH:mm`

### 5.4 底部输入区 (Footer Glass)
- 磨砂玻璃效果同顶部
- 内层：圆角大 pill 容器（`rounded-[2rem]`），白色背景
- 左侧：图片📷、附件📎、阅后即焚⏱ 三个圆形图标按钮
- 中央：透明 `textarea`，placeholder："输入消息，或拖入文件到聊天区域…"
- 右侧：蓝色渐变圆形发送按钮（`arrow_upward` 图标）

---

## 6. 登录页面 (Login Page)

### 6.1 背景
- 全屏渐变：`linear-gradient(135deg, #0a0f2e 0%, #0050cb 50%, #0a84ff 100%)`
- 背景装饰：两个大型半透明蓝色圆形（blur 渐变光晕），营造深空感

### 6.2 登录卡片
- 最大宽度：400px，居中
- 背景：`rgba(255, 255, 255, 0.08)`（玻璃态，frosted glass）
- 边框：`rgba(255, 255, 255, 0.15)`，1px
- 圆角：`rounded-3xl`（24px）
- 内边距：32px
- 阴影：`0 32px 64px rgba(0, 0, 0, 0.4)`

### 6.3 卡片内容
```
┌──────────────────────────────────┐
│         [Logo icon]              │
│         AnyDrop                  │
│    私有跨设备内容共享平台          │
│  ──────────────────────────────  │
│         输入密码继续使用           │
│  ┌──── 密码输入框 ─────────────┐  │
│  │ 🔒  ················  [👁] │  │
│  └────────────────────────────┘  │
│  ┌──────────────────────────────┐ │
│  │         登 录                │ │
│  └──────────────────────────────┘ │
│  ⚠️ 错误提示（如有）              │
└──────────────────────────────────┘
```

### 6.4 输入框样式
- 背景：`rgba(255, 255, 255, 0.1)`
- 边框：`rgba(255, 255, 255, 0.2)`（focus 时白色 / primary 色）
- 文字：白色
- placeholder：半透明白色

### 6.5 登录按钮
- 全宽，圆角 pill（`rounded-full`）
- 背景：白色渐变 → 按钮文字蓝色；或：蓝色渐变 → 白色文字
- 设计采用：白色背景 + 蓝色文字，hover 时微微上浮 + 阴影

---

## 7. 动画与过渡 (Motion)

| 动画 | 属性 | 缓动 | 持续时间 |
|------|------|------|----------|
| 消息气泡入场 | `translateY + scale + opacity` | `cubic-bezier(0.22, 1, 0.36, 1)` | 320ms |
| 侧边栏项激活竖条 | `height` | `cubic-bezier(0.34, 1.56, 0.64, 1)` | 250ms |
| Modal 弹出 | `translateY + scale + opacity` | `cubic-bezier(0.34, 1.56, 0.64, 1)` | 280ms |
| 按钮 hover | `translateY(-1px) + box-shadow` | `ease` | 150ms |
| 按钮 active | `scale(0.97)` | `ease` | 100ms |
| 登录卡片入场 | `translateY(24px) + opacity` | `cubic-bezier(0.22, 1, 0.36, 1)` | 600ms |

---

## 8. 响应式 (Responsive)

| 断点 | 行为 |
|------|------|
| ≥ 769px | 双栏布局（侧边栏 220px + 内容区 flex-1） |
| ≤ 768px | 单栏，侧边栏隐藏，仅显示内容区 |

---

## 9. 图标系统 (Icons)

所有图标使用 **Google Material Symbols**（Outlined 变体，`font-variation-settings` 控制 FILL 状态）：

| 场景 | 图标名 |
|------|--------|
| 品牌/同步 | `sync_alt` |
| 新建 | `add` |
| 聊天气泡 | `chat_bubble` |
| 搜索 | `search` |
| 设置/配置 | `settings` / `build` |
| 登出 | `logout` |
| 电脑 | `laptop_mac` |
| 手机 | `smartphone` |
| 图片 | `image` |
| 附件 | `attachment` |
| 阅后即焚 | `timer` |
| 发送 | `arrow_upward` |
| 下载 | `download` |
| 链接 | `link` |
| 文件 | `description` |
| 视频 | `videocam` |
| 时钟 | `schedule` |
| 锁 | `lock` |
