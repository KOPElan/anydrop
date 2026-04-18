namespace AnyDrop.Api;

public sealed record ApiEnvelope<T>(bool Success, T? Data, string? Error)
{
    public static ApiEnvelope<T> Ok(T data) => new(true, data, null);

    public static ApiEnvelope<T> Fail(string error) => new(false, default, error);
}
