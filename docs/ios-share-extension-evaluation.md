# iOS Share Extension 评估（S2-06，D4：只评估，不实施）

结论：**本轮不实施**。按 D4 定稿，MAUI 客户端本轮只做自用分发、不提交 App Store；
iOS Share Extension 的投入产出比在本轮不成立。若将来改为正式发布，再按下面立项。

## 现状

- iOS 端目前没有分享入口：`Platforms/iOS/` 下只有 `AppDelegate.cs`（处理 `anydrop://` 深链接），
  没有 Share Extension target，也没有 App Groups 配置。
- Android 端 S2-06 已修复：`ACTION_SEND` / `ACTION_SEND_MULTIPLE` 由 `MainActivity`
  接收、接收时复制进缓存、待分享内容落盘，进程被杀可恢复。
- iOS 用户目前分享到 AnyDrop 的唯一路径是 App 内手动选择文件上传。

## 若要实施，需要做什么

1. **新建 Share Extension target**（Xcode / MAUI 需手写 `.csproj` + `Info.plist`）：
   `NSExtensionPointIdentifier = com.apple.share-services`，声明支持的
   `NSExtensionActivationRule`（文本、图片、视频、文件），`NSExtensionMainStoryboard` 或代码 UI。
2. **App Groups**（`group.<bundle-id>.anydrop`）：Extension 与主 App 各是独立进程、沙箱隔离，
   待分享内容与缓存文件必须经共享容器传递；`FilePendingShareStore` 的 JSON 文件与缓存目录
   需改走 App Group 容器路径（目前写的是 `FileSystem.AppDataDirectory` / `CacheDirectory`，
   Extension 进程读不到）。
3. **Extension 内只做“接收 + 落盘”**：Extension 内存上限很低（约 120MB），不能在里面做上传；
   正确做法是把文件复制进共享容器、写一条待分享记录，主 App 下次启动时由 `Routes.razor`
   检测到并跳 `/share` 页消费。主 App 侧的消费逻辑（`ExternalSharePage`）可复用，
   只需把存储后端换成共享容器路径。
4. **签名与描述文件**：Extension 需要独立的 App ID + Provisioning Profile；
   自用分发（Ad Hoc / Developer ID 侧载）下每台设备都要装描述文件，维护成本高于 Android 侧载。
5. **测试**：需真机验证从 Photos / Files / Safari 分享单张、多张图片与视频，
   以及 Extension 被系统杀掉后主 App 恢复待分享内容的路径。

## 工作量估计

- Extension target + App Groups + 存储路径切换：约 2–3 人日。
- 真机验证（含多图、视频、杀进程恢复）：约 1 人日。
- 合计约 3–4 人日，不含 Apple 开发者账号续期、描述文件维护等持续成本。

## 暂缓的理由

- D4 已定：本轮 MAUI 只做自用分发，不走 App Store；Share Extension 的主要价值
  （让 iOS 用户从系统分享菜单直达）需要真机 + 账号 + 描述文件全套配合，收益集中在 iOS 重度用户，
  而 Elan 本人主力设备是 Android（见审计：Android 分享链是 P0，iOS 只是评估项）。
- Android 链已修好，iOS 用户仍可用 App 内上传完成同一任务，只是多两步操作。
- Extension 引入 App Groups 后，`FilePendingShareStore` 的路径假设会变，
  现在做等于给尚未确定的分发方式提前付架构成本。

## 触发重评估的条件

- D4 改变：决定正式发布 iOS 版（含 App Store 或 TestFlight）；或
- Elan 明确 iOS 分享是高频刚需，愿意承担账号与描述文件维护成本。
