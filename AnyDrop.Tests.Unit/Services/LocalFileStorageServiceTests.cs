using AnyDrop.Services;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.Services;

public class LocalFileStorageServiceTests
{
    private readonly LocalFileStorageService _service = new();

    [Fact]
    public async Task SaveFileAsync_ShouldThrowNotImplementedException()
    {
        var act = () => _service.SaveFileAsync(Stream.Null, "file.txt", "text/plain");
        await act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task GetFileAsync_ShouldThrowNotImplementedException()
    {
        var act = () => _service.GetFileAsync("missing/path");
        await act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task DeleteFileAsync_ShouldThrowNotImplementedException()
    {
        var act = () => _service.DeleteFileAsync("missing/path");
        await act.Should().ThrowAsync<NotImplementedException>();
    }
}
