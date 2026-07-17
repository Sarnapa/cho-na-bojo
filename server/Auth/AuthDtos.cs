using ChoNaBojo.Server.Data.Entities;

namespace ChoNaBojo.Server.Auth;

public sealed record RegisterRequest(
	string LoginEmail,
	string Password,
	string? ContactPhone,
	string? ContactEmail,
	CommunicatorPlatform? CommunicatorPlatform,
	string? CommunicatorHandle);

public sealed record LoginRequest(string LoginEmail, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresUtc);

public sealed record CurrentUserResponse(Guid UserId);
