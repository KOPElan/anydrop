namespace AnyDrop.App.Platform;

/// <summary>
/// 安全密钥存储的抽象（Android Keystore / iOS Keychain / Windows DPAPI）。
///
/// 与 <see cref="IPreferenceStore"/> 同样的理由：把 <c>SecureStorage</c> 这个
/// 平台原语收敛到接口后面，使 <c>SecureTokenStorage</c> 不再依赖条件编译，
/// 也让 Token 持久化逻辑第一次变得可单元测试。
/// </summary>
public interface ISecretStore
{
    /// <summary>读取密钥；不存在时返回 <see langword="null"/>。</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>写入密钥。</summary>
    Task SetAsync(string key, string value, CancellationToken ct = default);

    /// <summary>删除密钥；键不存在时不抛异常。</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);
}
