namespace AnyDrop.Models;

public sealed class Topic
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; } = int.MaxValue;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastMessageAt { get; set; }
}
