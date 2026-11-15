# 规格冻结（S3-05，2026-11-15）

## 决定

`docs/specs/` 下的 speckit 任务清单（001、006 等）自此**冻结为历史记录**，
不再作为跟踪方法。原因：清单写于功能开发初期，之后经历了审计
（52 项问题）与四阶段修复（S0–S3 共 34 个任务），实际工作早已按
审计 ID（SEC/DAT/CLI）与修复任务 ID（S0-xx…S3-xx）跟踪；清单里的
文件路径也已过期（客户端可测试逻辑移入 `AnyDrop.App.Core`，
测试移入 `AnyDrop.Tests.Unit/App/`，见下）。

本次按当前代码逐项核对了其中两份未打勾的清单
（`001-core-infra-mvp/tasks.md` 35 项、`006-maui-mobile-app/tasks.md` 97 项），
结果写在各自文件头的冻结小节里；核对时发现的两个真缺口记在末尾。
其余 speckit 清单（002–005、main）同样冻结，不再逐项补勾——
它们覆盖的功能均已被审计与修复任务覆盖或 superseded。

今后新增工作的跟踪方法：审计 ID + 修复任务 ID（S0/S1/S2/S3），
按阶段分支 + PR 记录；不再往 speckit 清单里加项。

## 冻结常量（与代码一致，不一致时测试失败）

| 常量 | 冻结值 | 位置（全部须一致） | 依据 |
|---|---|---|---|
| 上传上限 | **500 MB = 524288000 字节** | `AnyDrop.Shared/UploadLimits.cs`（代码回退值）、`AnyDrop/appsettings.json`、`AnyDrop/appsettings.Development.json`、`docker-compose.yml`（`Storage__MaxFileSizeBytes`，默认 524288000）、`.env.example`（`ANYDROP_MAX_FILE_SIZE=524288000`） | D2（Elan 2026-11-14 定稿，覆盖原 100MB 建议；1GB 回退值已删除） |
| 后台任务时区 | 配置键 `BackgroundTasks:TimeZoneId`，环境变量 **`BackgroundTasks__TimeZoneId`**，IANA ID（如 `Asia/Shanghai`）；未配置时用当前计算机时区（`TimeZoneInfo.Local`），**不是 UTC**；非法 ID 记 Error 日志后回退本机时区 | `AnyDrop/Services/BackgroundTaskTimeZone.cs`、`AnyDrop/appsettings.json` 无默认值（靠代码回退）、`README.md`（运维说明） | D6（Elan 2026-11-14 改定：环境变量配置，默认当前计算机时区） |

一致性检查：`AnyDrop.Tests.Unit/SpecFreezeTests.cs`——断言代码常量、
`appsettings.json`、`docker-compose.yml`、`.env.example` 四处上传上限
均为 524288000，且后台时区配置键按 D6 行为生效。改任一处而不改其余
处，测试即失败。

其他已定决策（D1/D3/D4/D5）同样冻结，见修复计划
`~/workspace/your_files/anydrop-fix-plan/anydrop-fix-plan.pdf` p.2，
此处不再重复：阅后即焚 = 发送后定时销毁；默认经 TLS 反向代理交付，
Cookie Secure + HTTPS 重定向 + HSTS 开启，明文 HTTP 仅作显式标注的
局域网例外；MAUI 仅自用分发；批量/自动删除先标记、7 天后物理删除。

## 核对时发现的两个真缺口（未在本轮修，记在这里）

1. **Android 分享缺少 `ACTION_SEND_MULTIPLE` 声明与 `launchMode`**
   （006 T076 部分完成）：`MainActivity.OnNewIntent` 能处理多分享，
   但 `AndroidManifest.xml` 只有三条 `ACTION_SEND` 过滤器
   （text/plain、image/*、*/*），没有 `SEND_MULTIPLE`，且 activity
   没有 `launchMode="singleTask"`。
   从其他 App 多选分享时系统不会提供 AnyDrop。修复是补一条
   intent-filter 与 launchMode，需真机验证，留给下一轮。
2. **客户端全局错误处理缺失**（006 T092 未完成）：
   App 组件里没有 `<ErrorBoundary>`，未处理异常无统一友好提示。
   各服务调用有各自的 try-catch，但不是统一的全局处理。
   同样留给下一轮；不阻塞冻结。

其余未完成项均为文档/形式类（见两份清单文件头的冻结小节）：
001 的 T004（spec 目录 README 未建，内容在根 README）、T023
（服务端从未采用 FluentUI，布局用自研 CSS）；
006 的 T095（XML doc 注释只覆盖部分服务）。
