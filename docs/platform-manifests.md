# 平台清单 / 权限对齐核查（S2-09）

最后核查：2026-11-15（Stage 2）。本文记录每次核查的结论；改动清单后，
若新增平台能力（相机拍摄、后台服务、新的系统 API），须重新核对本文件。

## 已修复

| # | 位置 | 问题 | 修复 |
|---|------|------|------|
| 1 | `AnyDrop.App/Platforms/iOS/Resources/PrivacyInfo.xcprivacy` | `NSPrivacyAccessedAPICategoryUserDefaults`（CA92.1）被注释掉，但应用确实在使用 Preferences API（`MauiPreferenceStore`：输入框草稿、主题设置；iOS 上即 NSUserDefaults） | 取消注释，原因 CA92.1 |
| 2 | `AnyDrop.App/Platforms/Android/AndroidManifest.xml` | 缺少 `READ_MEDIA_AUDIO`（图片/视频权限有，音频没有；选音频文件会失败） | 增加该权限 |
| 3 | 同上 | `package="com.peedan.anydrop"` 与 csproj 的 `ApplicationId=com.kopelan.anydrop` 不一致（构建时以后者为准，源码里的旧值易误导） | 改为 `com.kopelan.anydrop` |
| 4 | `AnyDrop.App/Platforms/MacCatalyst/Info.plist` | 缺少 `NSAppTransportSecurity`：iOS 侧已放行任意 HTTP（自托管场景），MacCatalyst 侧没有，ATS 会拦截到局域网 HTTP 服务端的连接 | 补与 iOS 侧相同的三项（`NSAllowsArbitraryLoads` / `NSAllowsLocalNetworking` / `NSAllowsArbitraryLoadsInWebContent`） |

## 已核查、无需改动

- **iOS `Info.plist` 用途说明**：`NSPhotoLibraryUsageDescription`、`NSCameraUsageDescription`、`NSMicrophoneUsageDescription` 均已存在。相机/麦克风目前无调用代码（未实现拍摄功能），描述保留作未来功能备用，不删除。
- **iOS `PrivacyInfo.xcprivacy` 其余三项**：FileTimestamp（C617.1）、SystemBootTime（35F9.1）、DiskSpace（E174.1）均为 MAUI 模板默认值，保留。应用不使用追踪（无 `NSPrivacyTracking` 相关项）。
- **Android 其余权限**：`INTERNET`、`ACCESS_NETWORK_STATE`、`POST_NOTIFICATIONS`（Android 13+ 通知）、`READ_EXTERNAL_STORAGE`（`maxSdkVersion=32`，API 33+ 改走分媒体权限）均已存在。`WRITE_EXTERNAL_STORAGE` 不需要（只写应用私有目录）；`VIBRATE`、`RECEIVE_BOOT_COMPLETED`、`FOREGROUND_SERVICE` 不需要（无对应功能）。`usesCleartextTraffic=true` 是有意的（D3：明文 HTTP 仅作局域网例外）。
- **MacCatalyst 用途说明与沙箱**：文件/照片选择走系统面板（NSOpenPanel / PHPicker），macOS 不要求用途说明；本项目按 D4 自用分发（签名侧载，不走 Mac App Store），不启用沙箱，因此不需要 `com.apple.security.files.user-selected.read-only` 等 entitlement，也就没有 entitlements 文件。若将来走商店分发，须重新评估沙箱 entitlements 与 MacCatalyst 的隐私清单（目前隐私清单只在 iOS 侧）。
- **Windows `Package.appxmanifest`**：仅有 `runFullTrust` 能力；`WindowsPackageType=None`（无包分发），能力不生效，`internetClient` 等无需声明。

## S3-06 包名与清单占位符核对（2026-11-15，Stage 3）

核对方式：源码一致性（`ManifestConsistencyTests`）+ 人工读清单；构建产物
（apk/ipa/msix 里的清单）本机无法核对——Linux 虚拟机没有 Android/iOS/
MacCatalyst/Windows workload，`AnyDrop.App/obj/Release/` 下只有 net10.0
验证构建，没有任何平台构建产物。产物核对是 CI `build-client` 的事：
打出包后解开清单，确认包名/版本与下表一致。

| 平台 | 源码值 | 构建时来源 | 结论 |
|---|---|---|---|
| Android `package` | `com.kopelan.anydrop` | csproj `ApplicationId` | 一致（测试断言） |
| Android `versionCode` | `1` | csproj `ApplicationVersion=1` | 一致（测试断言） |
| Android `versionName` | `1.0.0`（手写） | csproj `ApplicationDisplayVersion=$(Version)`，构建时覆盖 | 源码值只是回退占位；首版 v1.0.0 时恰好相等，版本递增后以标签为准，不必同步改清单 |
| csproj `ApplicationDisplayVersion` | `$(Version)` | Git 标签（S3-01） | 单一来源（测试断言），不得手写版本号 |
| iOS / MacCatalyst `Info.plist` | 无 `CFBundleIdentifier` / `CFBundleShortVersionString` / `CFBundleVersion` / `CFBundleName` | MAUI 构建时从 csproj 生成 | 正确：手写值会被覆盖或冲突（测试断言） |
| Windows `Package.appxmanifest` | `Identity Name="maui-package-name-placeholder"`、`Version="0.0.0.0"`、`DisplayName=$placeholder$` 等 | MAUI 构建时从 csproj（ApplicationId / ApplicationDisplayVersion / ApplicationTitle）替换 | 正确：全是模板占位符（测试断言）；`PhoneProductId` 沿用模板值，自用侧载（D4）不走商店，不需要真实产品 ID；若将来走商店分发，须按商店分配的值替换 |

已知缺口（S3-05 已记录，本轮不修）：Android 清单没有 `ACTION_SEND_MULTIPLE`
intent-filter，也没有 `launchMode="singleTask"`——`MainActivity.OnNewIntent`
能处理多文件分享，但系统不会把 AnyDrop 列为多文件分享目标。

## 本机无法验证的部分

Android / iOS / MacCatalyst 的平台构建均需对应 workload / 设备，Linux 虚拟机上无法编译验证。
上述文件仅做了 XML/plist 语法与内容断言（见提交说明）；真正的安装后行为
（权限弹窗、ATS 放行、隐私清单随包）属于 S2-11 真机回归门的一部分。
