using AnyDrop.Services;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.Services;

public sealed class LocalFileStorageServiceTests
{
    [Fact]
    public async Task SaveFileAsync_MvpImplementation_ThrowsNotSupportedException()
    {
        var service = new LocalFileStorageService();
        await using var stream = new MemoryStream([1, 2, 3]);

        var action = async () => await service.SaveFileAsync(stream, "file.txt", "text/plain");

        await action.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task GetFileAsync_MvpImplementation_ThrowsNotSupportedException()
    {
        var service = new LocalFileStorageService();

        var action = async () => await service.GetFileAsync("file.txt");

        await action.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task DeleteFileAsync_MvpImplementation_ThrowsNotSupportedException()
    {
        var service = new LocalFileStorageService();

        var action = async () => await service.DeleteFileAsync("file.txt");

        await action.Should().ThrowAsync<NotSupportedException>();
    }
}
