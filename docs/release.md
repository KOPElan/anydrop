# 发布流程（S3-01）

## 唯一版本源：Git 标签

版本号只由 Git 标签定。`Directory.Build.props` 默认 `0.0.0-dev`（本地开发构建），
发布构建由 release 工作流把标签去掉 `v` 前缀后传进 `VERSION`：

```
v1.0.0  ->  Version=1.0.0  ->  InformationalVersion=1.0.0+<提交短哈希>
```

`Directory.Build.targets` 在构建时把当前提交短哈希追加到
`InformationalVersion`。MAUI 的 `ApplicationDisplayVersion` 取同一 `$(Version)`。

## 任一产物怎么查到标签与提交

| 产物 | 查法 |
|---|---|
| 运行中的服务端 | `GET /health` 返回 `version` 字段（如 `1.0.0+abc1234`）；启动日志第一行也记版本号 |
| 容器镜像 | `org.opencontainers.image.version`（标签）与 `org.opencontainers.image.revision`（提交 SHA，由 docker/metadata-action 写入 label） |
| MAUI 侧载包 | 应用内「关于」/系统应用信息里的显示版本即 `$(Version)` |

## 发一个版本

```bash
# 1. 确认 main 已包含要发布的全部阶段，且 CI 全绿
# 2. 打标签并推送（标签推送即触发 .github/workflows/release.yml）
git tag -a v1.0.0 -m "AnyDrop v1.0.0：修复计划阶段 0-3"
git push origin v1.0.0
# 3. 工作流构建并推送镜像 ghcr.io/kopelan/anydrop:v1.0.0（另加 v1、v1.0 标签）
# 4. 部署端：.env 里 ANYDROP_VERSION=v1.0.0，docker compose pull && docker compose up -d
# 5. 验证：curl /health，version 字段应为 1.0.0+<该标签的提交短哈希>
```

## 镜像钉死版本

`AnyDrop/Dockerfile` 的基础镜像钉死具体补丁版本（当前
`aspnet:10.0.15`、`sdk:10.0.404`），不再用 `latest` / `10.0` 浮动标签。
升级补丁只改 Dockerfile 两行；digest 钉死是下一步（用
`docker buildx imagetools inspect mcr.microsoft.com/dotnet/aspnet:10.0.15`
查当前 digest 后写成 `aspnet:10.0.15@sha256:...` 形式，本机无 Docker 未在此验证）。

## 容器内存上限

`docker-compose.yml` 已设 `deploy.resources.limits.memory: 1024m`
（预留 256m）。缩略图大图解码是内存峰值来源（S1-02 已限流），上限防
并发上传时拖垮宿主机；超限时容器被 OOM 杀掉并按 `restart: unless-stopped`
重起。内存小于 1G 的宿主机请下调本值。

## v1.0.0 标签说明

第一个正式标签 `v1.0.0` 随修复计划阶段 3 在本地创建，指向阶段 3 分支尖端，
**尚未推送**。阶段 0–3 的 PR 合并进 main 后，须先确认 main 尖端与该标签
指向一致（不一致则删掉本地标签、在 main 上重打），再 `git push origin v1.0.0`
触发首次发布。切勿在未合并前推送该标签。
