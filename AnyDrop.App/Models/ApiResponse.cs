namespace AnyDrop.App.Models;

/// <summary>统一解析服务端 { success, data, error } envelope 的泛型响应记录。</summary>
public sealed record ApiResponse<T>(bool Success, T? Data, string? Error);
