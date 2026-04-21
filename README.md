# AnyDrop

> 私有、自托管的跨设备内容共享应用，基于 .NET 10 + Blazor 构建。

通过浏览器即可在任意设备间安全地保存与获取文字、图片、文件和链接，实时同步，无需依赖任何第三方云服务。

---

## 功能特点

- **跨设备实时同步** — 基于 SignalR（WebSocket），消息推送即时到达所有已登录设备
- **多类型内容** — 支持文字、图片、视频、任意文件、链接（含 OG 元数据预览）
- **主题分组** — 将内容按主题（频道）组织，支持置顶、归档、删除
- **主题搜索** — 全文搜索、按日期查找、按类型（图片/视频/文件/链接）分类浏览
- **阅后即焚** — 消息可设为"阅后即焚"，阅读后自动销毁
- **单用户私有部署** — 首次访问完成初始化设置，密码保存在本地数据库，不外发
- **容器化就绪** — 提供 Dockerfile 与 docker-compose.yml，一命令启动

---

## 技术栈

| 层 | 技术 |
|---|---|
| 框架 | .NET 10 · Blazor Web App（Interactive Server） |
| 数据库 | SQLite（EF Core） |
| 实时通信 | ASP.NET Core SignalR |
| 样式 | Tailwind CSS v4 |
| 认证 | JWT Bearer + Cookie |
| 容器 | Docker / Docker Compose |

---

## 快速开始

### 方式一：Docker Compose（推荐）

**1. 克隆仓库**

```bash
git clone https://github.com/KOPElan/anydrop.git
cd anydrop
```

**2. 创建环境变量文件**

```bash
cp .env.example .env   # 若不存在则手动创建
```

`.env` 文件内容示例：

```dotenv
# 必填：JWT 签名密钥（建议 32 位以上随机字符串）
ANYDROP_JWT_SECRET=your-very-long-random-secret-key

# 可选：上传文件大小上限（字节），默认 100 MB
ANYDROP_MAX_FILE_SIZE=104857600

# 可选：登录令牌有效期（小时），默认 24 小时
ANYDROP_TOKEN_EXPIRY_HOURS=24
```

**3. 启动服务**

```bash
docker compose up -d
```

**4. 初始化账号**

首次启动后，在浏览器访问 `http://localhost:8080/setup`，设置登录密码。

> 数据（SQLite 数据库 + 上传文件）持久化到 Docker volume `anydrop-data`，容器重启不会丢失。

---

### 方式二：本地源码运行

#### 前置要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js ≥ 20](https://nodejs.org/)（用于 Tailwind CSS 编译）

#### 步骤

```bash
# 1. 克隆仓库
git clone https://github.com/KOPElan/anydrop.git
cd anydrop

# 2. 安装前端依赖（Tailwind CSS）
npm install

# 3. 配置 JWT 密钥（推荐使用 .NET User Secrets，避免提交密钥到版本库）
cd AnyDrop
dotnet user-secrets set "Auth:JwtSecret" "your-very-long-random-secret-key"
cd ..

# 4. 启动应用
dotnet run --project AnyDrop
```

应用默认监听 `http://localhost:5002`。

首次运行请访问 `http://localhost:5002/setup` 完成初始化。

---

## 目录结构

```
AnyDrop/
├── Api/                  # Minimal API 端点（/api/v1/）
├── Components/
│   ├── Pages/            # 路由页面（Home、TopicSearch、Login、Setup 等）
│   ├── Layout/           # 布局组件（MainLayout、TopicSidebar 等）
│   └── _Imports.razor    # 全局 using
├── Data/                 # EF Core DbContext 与迁移
├── Hubs/                 # SignalR Hub
├── Models/               # 实体与 DTO
├── Services/             # 业务逻辑服务（接口 + 实现）
├── wwwroot/              # 静态资源（CSS、JS）
└── Program.cs            # 依赖注入与中间件管道
```

---

## 环境变量参考

| 变量名 | 说明 | 默认值 |
|---|---|---|
| `Auth__JwtSecret` | JWT 签名密钥（**必填**） | — |
| `Auth__TokenExpiryHours` | 登录令牌有效期（小时） | `24` |
| `Auth__LoginMaxFailures` | 登录失败锁定阈值 | `5` |
| `Auth__LoginCooldownSeconds` | 登录冷却时间（秒） | `60` |
| `Storage__DatabasePath` | SQLite 数据库路径 | `data/anydrop.db` |
| `Storage__BasePath` | 上传文件存储目录 | `data/files` |
| `Storage__MaxFileSizeBytes` | 单文件大小上限（字节） | `104857600`（100 MB） |
| `ASPNETCORE_URLS` | Kestrel 监听地址 | `http://+:5002`（容器内为 `http://+:8080`） |

---

## 数据备份

持久化数据存放在 Docker volume `anydrop-data`（挂载到容器的 `/data`），包含：

- `/data/anydrop.db` — SQLite 数据库（用户、主题、消息元数据）
- `/data/files/` — 上传的文件

备份示例：

```bash
docker run --rm \
  -v anydrop-data:/data:ro \
  -v $(pwd)/backup:/backup \
  alpine tar czf /backup/anydrop-backup-$(date +%Y%m%d).tar.gz /data
```

---

## 开发指南

### 运行测试

```bash
# 单元测试
dotnet test AnyDrop.Tests.Unit

# E2E 测试（需先启动应用）
dotnet test AnyDrop.Tests.E2E
```

### 数据库迁移

```bash
# 添加新迁移
dotnet ef migrations add <MigrationName> --project AnyDrop

# 应用迁移
dotnet ef database update --project AnyDrop
```

### 构建容器镜像

```bash
docker build -t anydrop .
docker run -p 8080:8080 -e Auth__JwtSecret=your-secret anydrop
```

---

## License

[MIT](LICENSE)
