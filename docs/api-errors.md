# API 错误码（S3-06）

服务端错误不再把中文原文直达英文界面：所有 `/api/v1` 失败响应在信封里同时带
`Error`（原文，兼容旧客户端与日志）与 `ErrorCode`（snake_case 码，客户端按码
查本地化文案）。码未知或缺失时，客户端回退到 `Error` 原文；两者都没有时用
页面默认文案。

## 码表

码的唯一定义在 `AnyDrop.Shared/ApiErrorCodes.cs`（服务端与 MAUI 客户端共用）。
资源键 = `Error_` + 码（见 `ApiErrorCodes.ResourceKey`）。

| 分组 | 码 | 含义 |
|---|---|---|
| 认证 | `unauthorized` / `forbidden` / `invalid_credentials` / `wrong_password` / `user_not_found` / `invalid_refresh_token` / `logout_failed` / `auth_failed` / `get_user_failed` | 401/403 与认证类失败 |
| 初始化 | `setup_required` / `setup_already_done` | 未初始化 / 已初始化 |
| 限流 | `too_many_requests` | 速率限制（429） |
| 校验 | `invalid_nickname` / `password_mismatch` / `password_too_short` / `password_same_as_old` / `content_required` / `topic_required` / `file_required` / `ids_required` / `items_required` / `action_required` / `search_query_required` / `invalid_months` / `invalid_date_range` / `invalid_time_zone` / `unsupported_language` / `invalid_request` | 请求参数无效 |
| 不存在 | `topic_not_found` / `message_not_found` / `message_expired` / `message_has_no_file` / `file_not_found` / `thumbnail_not_ready` / `thumbnail_not_found` / `record_not_found` / `upload_session_not_found` | 404 类 |
| 操作 | `update_nickname_failed` / `update_password_failed` / `update_settings_failed` / `upload_failed` / `send_message_failed` / `create_topic_failed` / `update_topic_failed` / `reorder_topics_failed` / `resolve_failed` / `batch_delete_too_large` / `storage_full` | 服务端操作失败 |
| 安全 | `invalid_origin` | Origin 校验失败（403） |

## 客户端映射规则

- MAUI：`AnyDrop.App.Core/Services/ApiErrorMessages.Localize(code, lookup, fallback)`，
  `lookup` 由页面提供（`IStringLocalizer<AppStrings>` 查 `Error_<code>`，键不
  存在返回 null，不抛异常）。Core 项目没有引用 Localization 包，查词函数由调用方
  注入，所以这层不依赖 resx 生成器。
- 上传失败走 `UploadFailedException`（`AnyDrop.App.Core/Services/UploadFailedException.cs`，
  带 `ErrorCode`），`FileUploadService` 在服务端明确拒绝（4xx）时抛出它，5xx/网络
  异常仍走重试循环；重试循环遇到它直接抛，不重试。
- 登录/初始化失败：`AuthService.PostAuthAsync` 把信封的 `ErrorCode` 透传到
  `AppAuthResult.ErrorCode`，页面按码显示；失败时不再直接显示 `result.Error`。
- Web Blazor 页面不走 API 信封，直接调用服务端服务，错误文案仍是中文原文——
  Web 界面目前只有中文，不在本轮本地化范围内。

## 本地化文案

`Error_<code>` 词条在三份 resx 里：`AppStrings.resx`（默认 zh-CN）、
`AppStrings.en.resx`、`AppStrings.zh-TW.resx`。新增错误码时三处都要补，
否则该语言下回退到服务端原文。

## 例外：刻意不透露细节的失败

- Refresh Token 轮换失败统一返回 `invalid_refresh_token`，不区分“token 无效”
  与“检测到重放”——调用方无法从响应推断是否触发了重放检测（见
  `AuthEndpoints.RefreshAsync` 注释）。
- `SameOriginMiddleware` 的 403 响应不走信封（中间件在路由之外），只带
  `invalid_origin` 文本；浏览器端按状态码处理即可。
