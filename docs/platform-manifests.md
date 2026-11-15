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

## 本机无法验证的部分

Android / iOS / MacCatalyst 的平台构建均需对应 workload / 设备，Linux 虚拟机上无法编译验证。
上述文件仅做了 XML/plist 语法与内容断言（见提交说明）；真正的安装后行为
（权限弹窗、ATS 放行、隐私清单随包）属于 S2-11 真机回归门的一部分。
