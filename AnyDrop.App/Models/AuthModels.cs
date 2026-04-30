namespace AnyDrop.App.Models;

public sealed record SetupStatusDto(bool RequiresSetup);

public sealed record SetupRequest(string Nickname, string Password, string ConfirmPassword);

public sealed record LoginRequest(string Password);

public sealed record LoginResponse(bool Success, string? Token, DateTimeOffset? ExpiresAt, string? Error);

public sealed record UserProfileDto(string Nickname);
