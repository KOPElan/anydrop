using AnyDrop.App.Services;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.App;

/// <summary>
/// S2-08：通知 ID 由话题 Guid 稳定导出——同一话题始终同一 ID（覆盖旧通知），
/// 不同话题不同 ID，永不为负（旧实现 Math.Abs(int.MinValue) 会抛 OverflowException）。
/// </summary>
public class NotificationIdsTests
{
    [Fact]
    public void ForTopic_SameTopic_Stable()
    {
        var topicId = Guid.NewGuid();
        NotificationIds.ForTopic(topicId).Should().Be(NotificationIds.ForTopic(topicId));
    }

    [Fact]
    public void ForTopic_NonNegative()
    {
        for (var i = 0; i < 1000; i++)
        {
            NotificationIds.ForTopic(Guid.NewGuid()).Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void ForTopic_DifferentTopics_DifferentIds()
    {
        var a = NotificationIds.ForTopic(Guid.NewGuid());
        var b = NotificationIds.ForTopic(Guid.NewGuid());
        a.Should().NotBe(b);
    }
}
