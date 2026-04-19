namespace AnyDrop.Models;

public sealed class SystemSettings
{
    public Guid Id { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public bool AutoFetchLinkPreview { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
