using AnyDrop.App.Infrastructure;
using AnyDrop.App.Models;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.App;

/// <summary>S2-06：待分享内容持久化——进程被杀重启后仍能恢复；消费后清理缓存文件。</summary>
public class PendingShareStoreTests : IDisposable
{
    private readonly string _appDataDir;
    private readonly string _cacheDir;

    public PendingShareStoreTests()
    {
        var root = Path.Combine(Path.GetTempPath(), $"anydrop-pending-share-tests-{Guid.NewGuid():N}");
        _appDataDir = Path.Combine(root, "appdata");
        _cacheDir = Path.Combine(root, "cache");
        Directory.CreateDirectory(_appDataDir);
        Directory.CreateDirectory(_cacheDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_appDataDir)!, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task SaveAsync_PersistsToDisk_NewInstanceReadsItBack()
    {
        var cachedFile = Path.Combine(_cacheDir, "photo.jpg");
        await File.WriteAllTextAsync(cachedFile, "fake image");
        var store = new FilePendingShareStore(_appDataDir, _cacheDir);
        await store.SaveAsync(new SharedContent("hello", [new SharedFile(cachedFile, "image/jpeg")]));

        // 模拟进程被杀重启：新建实例从磁盘恢复
        var reopened = new FilePendingShareStore(_appDataDir, _cacheDir);

        reopened.HasContent.Should().BeTrue();
        reopened.Content!.Text.Should().Be("hello");
        reopened.Content.Files.Should().HaveCount(1);
        reopened.Content.Files[0].Path.Should().Be(cachedFile);
        reopened.Content.Files[0].MimeType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task SaveAsync_TextOnly_RestoresWithoutFiles()
    {
        var store = new FilePendingShareStore(_appDataDir, _cacheDir);
        await store.SaveAsync(new SharedContent("just text", []));

        var reopened = new FilePendingShareStore(_appDataDir, _cacheDir);

        reopened.Content!.Text.Should().Be("just text");
        reopened.Content.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearAsync_DeletesCachedFilesAndRecord()
    {
        var cachedFile = Path.Combine(_cacheDir, "video.mp4");
        await File.WriteAllTextAsync(cachedFile, "fake video");
        var store = new FilePendingShareStore(_appDataDir, _cacheDir);
        await store.SaveAsync(new SharedContent(null, [new SharedFile(cachedFile, "video/mp4")]));

        await store.ClearAsync();

        store.HasContent.Should().BeFalse();
        File.Exists(cachedFile).Should().BeFalse();
        File.Exists(Path.Combine(_appDataDir, "pending-share.json")).Should().BeFalse();

        var reopened = new FilePendingShareStore(_appDataDir, _cacheDir);
        reopened.HasContent.Should().BeFalse();
    }

    [Fact]
    public async Task ClearAsync_NeverDeletesFilesOutsideCacheDirectory()
    {
        var outsideFile = Path.Combine(_appDataDir, "not-a-cache-file.txt");
        await File.WriteAllTextAsync(outsideFile, "do not delete");
        var store = new FilePendingShareStore(_appDataDir, _cacheDir);
        await store.SaveAsync(new SharedContent(null, [new SharedFile(outsideFile, "text/plain")]));

        await store.ClearAsync();

        File.Exists(outsideFile).Should().BeTrue();
        store.HasContent.Should().BeFalse();
    }

    [Fact]
    public void Constructor_CorruptFile_StartsEmptyInsteadOfThrowing()
    {
        File.WriteAllText(Path.Combine(_appDataDir, "pending-share.json"), "{ broken json");

        var store = new FilePendingShareStore(_appDataDir, _cacheDir);

        store.HasContent.Should().BeFalse();
    }

    [Fact]
    public void NotificationTopicId_IsInMemoryOnly()
    {
        var store = new FilePendingShareStore(_appDataDir, _cacheDir);
        store.NotificationTopicId = Guid.NewGuid();

        var reopened = new FilePendingShareStore(_appDataDir, _cacheDir);

        reopened.NotificationTopicId.Should().BeNull();
    }
}
