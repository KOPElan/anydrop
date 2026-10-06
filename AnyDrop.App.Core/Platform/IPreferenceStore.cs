namespace AnyDrop.App.Platform;

/// <summary>
/// 键值偏好存储的抽象。
///
/// 引入它的原因：<c>Microsoft.Maui.Storage.Preferences</c> 只能在 MAUI 的
/// 平台目标框架下编译，此前业务代码用 <c>#if ANDROID || IOS || ...</c> 包住它，
/// 并在 <c>#else</c> 分支里退化成一个内存字典。这带来两个问题：
/// 一是业务逻辑里混入了平台条件编译，二是同一份代码在不同程序集里会静默走上
/// 不同分支（持久化 vs 不持久化），极易产生难以察觉的回归。
///
/// 现在把平台原语收敛到本接口：MAUI 端提供 <c>MauiPreferenceStore</c>，
/// 测试端提供内存实现，业务代码完全平台无关。
/// </summary>
public interface IPreferenceStore
{
    /// <summary>读取键值；不存在时返回 <paramref name="defaultValue"/>。</summary>
    string? Get(string key, string? defaultValue = null);

    /// <summary>写入键值。</summary>
    void Set(string key, string value);
}
