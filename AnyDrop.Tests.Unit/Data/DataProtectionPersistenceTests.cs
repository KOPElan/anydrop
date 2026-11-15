using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace AnyDrop.Tests.Unit.Data;

/// <summary>
/// S1-05（DAT-11）：Data Protection 密钥持久化到磁盘后，
/// 重建 provider（模拟容器重建）仍能解开旧 provider 加密的数据，
/// 即原有登录态（Cookie / 防伪令牌）不失效。
/// </summary>
public class DataProtectionPersistenceTests
{
    private static IDataProtectionProvider CreateProvider(string keysDir)
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("AnyDrop")
            .PersistKeysToFileSystem(new DirectoryInfo(keysDir));
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    [Fact]
    public void Protect_ThenRebuildProviderFromSameKeysDir_Unprotects()
    {
        var keysDir = Path.Combine(Path.GetTempPath(), $"anydrop-dp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keysDir);

        try
        {
            var protectorA = CreateProvider(keysDir).CreateProtector("test-purpose");
            var ciphertext = protectorA.Protect("hello");

            // 密钥文件应已落盘，而不是只在内存里
            Directory.GetFiles(keysDir).Should().NotBeEmpty();

            // 模拟容器重建：全新 provider，只共享 keys 目录
            var protectorB = CreateProvider(keysDir).CreateProtector("test-purpose");

            protectorB.Unprotect(ciphertext).Should().Be("hello");
        }
        finally
        {
            Directory.Delete(keysDir, true);
        }
    }

    [Fact]
    public void Protect_ThenRebuildProviderFromEmptyKeysDir_FailsToUnprotect()
    {
        // 反向证明：不是“本来就能解开”——密钥不在了就解不开，
        // 所以备份丢了 keys 目录真的会掉登录。
        var keysDirA = Path.Combine(Path.GetTempPath(), $"anydrop-dp-{Guid.NewGuid():N}");
        var keysDirB = Path.Combine(Path.GetTempPath(), $"anydrop-dp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keysDirA);
        Directory.CreateDirectory(keysDirB);

        try
        {
            var protectorA = CreateProvider(keysDirA).CreateProtector("test-purpose");
            var ciphertext = protectorA.Protect("hello");

            var protectorB = CreateProvider(keysDirB).CreateProtector("test-purpose");

            var act = () => protectorB.Unprotect(ciphertext);
            act.Should().Throw<Exception>();
        }
        finally
        {
            Directory.Delete(keysDirA, true);
            Directory.Delete(keysDirB, true);
        }
    }
}
