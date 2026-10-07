using System.Security.Cryptography;
using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// 密码哈希的测试。
///
/// 重点在**向后兼容**：哈希格式从「硬编码迭代次数的裸 base64」改为自描述串之后，
/// 既有数据库里的旧哈希必须仍能校验通过，并且要被标记为需要升级——
/// 否则这次改动会让所有已部署实例无法登录。
/// </summary>
public class PasswordHasherServiceTests
{
    /// <summary>用较低的迭代次数构造，避免拖慢测试；参数本身由配置决定。</summary>
    private static PasswordHasherService CreateSut(int iterations = 1_000)
        => new(Options.Create(new AuthOptions { PasswordHashIterations = iterations }));

    [Fact]
    public void HashPassword_ThenVerify_ReturnsSuccess()
    {
        var sut = CreateSut();
        var (hash, salt) = sut.HashPassword("P@ssw0rd!");

        sut.VerifyPassword("P@ssw0rd!", hash, salt).Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void HashPassword_ProducesSelfDescribingHash()
    {
        var sut = CreateSut(iterations: 4_321);
        var (hash, _) = sut.HashPassword("P@ssw0rd!");

        // 算法与迭代次数都在哈希串里，校验时不必依赖当前配置
        hash.Should().StartWith("pbkdf2-sha256$4321$");
    }

    [Fact]
    public void HashPassword_SamePasswordTwice_ProducesDifferentHashes()
    {
        var sut = CreateSut();

        var first = sut.HashPassword("P@ssw0rd!");
        var second = sut.HashPassword("P@ssw0rd!");

        first.Hash.Should().NotBe(second.Hash, "每次都应使用新的随机盐");
        first.Salt.Should().NotBe(second.Salt);
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFailed()
    {
        var sut = CreateSut();
        var (hash, salt) = sut.HashPassword("P@ssw0rd!");

        sut.VerifyPassword("Wrong", hash, salt).Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void VerifyPassword_LegacyUnprefixedHash_IsAcceptedAndFlaggedForRehash()
    {
        // 模拟升级前写入的哈希：整串就是 base64，迭代次数为旧的 100_000
        const string password = "P@ssw0rd!";
        var salt = RandomNumberGenerator.GetBytes(16);
        var legacyHash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, 100_000, HashAlgorithmName.SHA256, 32);

        var sut = CreateSut(iterations: 600_000);

        var result = sut.VerifyPassword(
            password, Convert.ToBase64String(legacyHash), Convert.ToBase64String(salt));

        result.Should().Be(
            PasswordVerificationResult.SuccessRehashNeeded,
            "旧格式必须仍可登录，但要提示调用方升级哈希");
    }

    [Fact]
    public void VerifyPassword_LegacyHashWithWrongPassword_ReturnsFailed()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var legacyHash = Rfc2898DeriveBytes.Pbkdf2(
            "P@ssw0rd!", salt, 100_000, HashAlgorithmName.SHA256, 32);

        var sut = CreateSut();

        sut.VerifyPassword("Wrong", Convert.ToBase64String(legacyHash), Convert.ToBase64String(salt))
            .Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void VerifyPassword_HashWithLowerIterations_RequestsRehash()
    {
        // 用旧参数创建的哈希，在提高参数后应被标记为需要升级
        var oldParameters = CreateSut(iterations: 1_000);
        var (hash, salt) = oldParameters.HashPassword("P@ssw0rd!");

        var currentParameters = CreateSut(iterations: 5_000);

        currentParameters.VerifyPassword("P@ssw0rd!", hash, salt)
            .Should().Be(PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Fact]
    public void VerifyPassword_HashWithHigherIterations_IsStillAcceptedWithoutRehash()
    {
        // 参数被调低（或哈希来自更高参数的实例）时不应强制降级重算
        var stronger = CreateSut(iterations: 5_000);
        var (hash, salt) = stronger.HashPassword("P@ssw0rd!");

        var weaker = CreateSut(iterations: 1_000);

        weaker.VerifyPassword("P@ssw0rd!", hash, salt).Should().Be(PasswordVerificationResult.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!")]
    [InlineData("pbkdf2-sha256$0$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]      // 迭代次数非法
    [InlineData("pbkdf2-sha256$-1$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]     // 迭代次数为负
    [InlineData("pbkdf2-sha256$1000$AAAA")]                                           // 哈希过短
    [InlineData("md5$1000$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]             // 未知算法
    public void VerifyPassword_MalformedHash_ReturnsFailed(string passwordHash)
    {
        var sut = CreateSut();

        sut.VerifyPassword("P@ssw0rd!", passwordHash, Convert.ToBase64String(new byte[16]))
            .Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void VerifyPassword_MalformedSalt_ReturnsFailed()
    {
        var sut = CreateSut();
        var (hash, _) = sut.HashPassword("P@ssw0rd!");

        sut.VerifyPassword("P@ssw0rd!", hash, "not-base64!!").Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void HashPassword_EmptyPassword_Throws()
    {
        var sut = CreateSut();

        var act = () => sut.HashPassword("  ");

        act.Should().Throw<ArgumentException>();
    }
}
