using AnyDrop.App.Services;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.App;

/// <summary>S2-05：离线发件箱——持久化、按序补发、失败保留、损坏文件不阻塞。</summary>
public class OutboxServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"anydrop-outbox-tests-{Guid.NewGuid():N}");

    public OutboxServiceTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 清理失败不影响测试结果 */ }
    }

    [Fact]
    public async Task EnqueueAsync_PersistsToDisk_NewInstanceReadsItBack()
    {
        var topicId = Guid.NewGuid();
        var service = new OutboxService(_dir);
        await service.EnqueueAsync(topicId, "hello offline", burnAfterReading: false);

        var reopened = new OutboxService(_dir);
        var pending = await reopened.GetPendingAsync();

        pending.Should().HaveCount(1);
        pending[0].TopicId.Should().Be(topicId);
        pending[0].Content.Should().Be("hello offline");
        pending[0].BurnAfterReading.Should().BeFalse();
    }

    [Fact]
    public async Task DrainAsync_SendsInEnqueueOrder_AndRemovesSentItems()
    {
        var topicId = Guid.NewGuid();
        var service = new OutboxService(_dir);
        await service.EnqueueAsync(topicId, "first", false);
        await Task.Delay(10);
        await service.EnqueueAsync(topicId, "second", true);

        var sent = new List<string>();
        var count = await service.DrainAsync(m =>
        {
            sent.Add(m.Content);
            return Task.CompletedTask;
        });

        count.Should().Be(2);
        sent.Should().Equal("first", "second");
        (await service.GetPendingAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DrainAsync_StopsOnFailure_AndKeepsRemainingItems()
    {
        var topicId = Guid.NewGuid();
        var service = new OutboxService(_dir);
        await service.EnqueueAsync(topicId, "ok", false);
        await service.EnqueueAsync(topicId, "boom", false);
        await service.EnqueueAsync(topicId, "later", false);

        var sent = new List<string>();
        var count = await service.DrainAsync(m =>
        {
            if (m.Content == "boom")
            {
                throw new HttpRequestException("network down");
            }

            sent.Add(m.Content);
            return Task.CompletedTask;
        });

        count.Should().Be(1);
        sent.Should().Equal("ok");
        var pending = await service.GetPendingAsync();
        pending.Select(m => m.Content).Should().Equal("boom", "later");
    }

    [Fact]
    public async Task DrainAsync_EmptyOutbox_ReturnsZero()
    {
        var service = new OutboxService(_dir);

        var count = await service.DrainAsync(_ => Task.CompletedTask);

        count.Should().Be(0);
    }

    [Fact]
    public async Task RemoveAsync_RemovesOnlyThatItem()
    {
        var topicId = Guid.NewGuid();
        var service = new OutboxService(_dir);
        await service.EnqueueAsync(topicId, "a", false);
        await service.EnqueueAsync(topicId, "b", false);
        var pending = await service.GetPendingAsync();

        await service.RemoveAsync(pending[0].Id);

        (await service.GetPendingAsync()).Select(m => m.Content).Should().Equal("b");
    }

    [Fact]
    public async Task GetPendingAsync_CorruptFile_ReturnsEmptyInsteadOfThrowing()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "outbox.json"), "{ not json !!!");

        var service = new OutboxService(_dir);
        var pending = await service.GetPendingAsync();

        pending.Should().BeEmpty();
        // 损坏文件不阻塞后续写入
        await service.EnqueueAsync(Guid.NewGuid(), "after-corruption", false);
        (await service.GetPendingAsync()).Should().HaveCount(1);
    }
}
