# Research: 认证、登录与设置功能

## Decision 1: 页面认证采用 Cookie，API 认证采用 JWT Bearer（双通道）

- Decision: Blazor 页面使用 Cookie 会话，Minimal API 使用 JWT Bearer。
- Rationale: Blazor Server 与 Cookie 集成成熟，页面跳转和重定向体验更自然；API 侧 JWT 便于移动端与脚本调用。
- Alternatives considered:
  - 仅 Cookie：不满足“API 需要 JWT 认证”约束。
  - 仅 JWT：页面重定向与会话管理复杂度更高。

## Decision 2: JWT 立即失效采用 SessionVersion（已澄清）

- Decision: JWT 内携带 `sessionVersion`，服务端存储当前版本；登出或改密时版本递增。
- Rationale: 不维护黑名单，状态存储轻量；可在下一次请求立即拒绝旧 Token。
- Alternatives considered:
  - JWT 黑名单：实现复杂，存储和清理成本高。
  - 纯短时 JWT：无法满足“立即失效”。

## Decision 3: 首次配置向导通过“唯一用户约束 + 事务”保障并发幂等

- Decision: `Users` 表设置单用户约束（例如固定主键或唯一标记），创建用户时事务提交；并发第二请求失败并返回“已完成初始化”。
- Rationale: 满足边界场景“向导并发提交两次”。
- Alternatives considered:
  - 仅应用层 if-check：并发窗口中仍可能双写。

## Decision 4: 密码存储采用强哈希（PBKDF2）+ 随机 Salt

- Decision: 使用 .NET 内置密码哈希方案（PBKDF2），每个用户独立 Salt。
- Rationale: 符合 OWASP 与宪法安全条款；不引入额外第三方依赖。
- Alternatives considered:
  - SHA256/MD5：不适合密码存储，抗暴力破解能力弱。

## Decision 5: 登录失败限流按“单用户 + 客户端 IP”维度计数

- Decision: 5 次失败触发 60 秒冷却；冷却期间直接返回统一错误。
- Rationale: 贴合局域网单用户场景，阻断暴力破解且避免永久锁死。
- Alternatives considered:
  - 永久锁定：可用性风险高。
  - 无限制：不满足 FR-017。

## Decision 6: 未认证页面访问统一通过中间件/授权策略重定向到 `/login`

- Decision: 对受保护页面启用授权策略，未认证自动带 `returnUrl` 跳转登录页。
- Rationale: 满足 FR-008、US3，且用户体验一致。
- Alternatives considered:
  - 页面内手动判断：易遗漏且不一致。

## Decision 7: 安全开关 `AutoFetchLinkPreview` 作为全局持久化设置

- Decision: 在 `SystemSettings` 单行表保存开关；链接预览服务执行前先读取该值。
- Rationale: 满足“修改立即生效、重启持久化”。
- Alternatives considered:
  - appsettings 配置：运行时不可变更，不满足设置页需求。

## Decision 8: API 错误响应沿用统一 Envelope

- Decision: 认证相关端点也返回 `{ success, data, error }` 结构，401/409 等状态码保留。
- Rationale: 保持现有 API 一致性，降低客户端处理复杂度。
- Alternatives considered:
  - 原生 ProblemDetails：与项目现有约定不一致。
