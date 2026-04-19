using AnyDrop.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace AnyDrop.Tests.Unit.Services;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _basePath = Path.Combine(AppContext.BaseDirectory, "test-storage", Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorageService _service;

    public LocalFileStorageServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:BasePath"] = _basePath
            })
            .Build();

        _service = new LocalFileStorageService(configuration);
    }

    [Fact]
    public async Task SaveAndGetFileAsync_ShouldRoundTrip()
    {
        await using var source = new MemoryStream([1, 2, 3, 4]);
        var savedPath = await _service.SaveFileAsync(source, "demo.bin", "application/octet-stream");

        await using var readStream = await _service.GetFileAsync(savedPath);
        using var reader = new MemoryStream();
        await readStream.CopyToAsync(reader);

        reader.ToArray().Should().Equal([1, 2, 3, 4]);
    }

    [Fact]
    public async Task DeleteFileAsync_ShouldRemoveFile()
    {
        await using var source = new MemoryStream([8, 9]);
        var savedPath = await _service.SaveFileAsync(source, "demo.bin", "application/octet-stream");

        await _service.DeleteFileAsync(savedPath);

        var act = () => _service.GetFileAsync(savedPath);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, true);
        }
    }
}
