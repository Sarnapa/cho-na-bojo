namespace ChoNaBojo.App.Services.Auth;

/// <summary>
/// Persists and retrieves the token pair from platform secure storage. Reads tolerate any
/// failure (corrupted keystore, platform quirk) by returning <c>null</c> — callers treat
/// that as "no session" rather than a crash.
/// </summary>
public interface ITokenStore
{
	Task<AuthSession?> LoadAsync();
	Task SaveAsync(AuthSession session);
	Task ClearAsync();
}
