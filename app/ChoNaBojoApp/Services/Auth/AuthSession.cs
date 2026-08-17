namespace ChoNaBojo.App.Services.Auth;

/// <summary>
/// The client's in-memory view of a signed-in session: the current token pair plus the
/// access token's expiry, used both for attaching the bearer and for the pre-flight
/// expiry check in <see cref="AuthenticatingHttpMessageHandler"/>.
/// </summary>
public sealed record AuthSession(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresUtc);
