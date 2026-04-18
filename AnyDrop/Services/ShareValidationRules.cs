namespace AnyDrop.Services;

/// <summary>
/// Defines shared validation limits for share workflows.
/// </summary>
public static class ShareValidationRules
{
    /// <summary>
    /// Maximum supported text content length.
    /// </summary>
    public const int MaxTextLength = 10_000;

    /// <summary>
    /// Maximum supported recent query count.
    /// </summary>
    public const int MaxRecentCountLimit = 200;
}
