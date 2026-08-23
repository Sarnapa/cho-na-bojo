using System.Text.Json;

namespace ChoNaBojo.App.Services.Auth;

/// <summary>
/// <see cref="SecureStorage"/>-backed implementation of <see cref="ITokenStore"/>. No secrets
/// are ever logged. See ui-guidelines/plan Critical Implementation Details for the Android
/// 10+ main-thread requirement on the first <see cref="SecureStorage"/> access — callers are
/// responsible for invoking this on the main thread during startup.
/// The whole session is stored under a single key so a write is atomic: a partial save can
/// never leave a new access token beside a stale refresh token.
/// </summary>
public class TokenStore: ITokenStore
{
	#region Private fields
	private const string SessionKey = "auth_session";
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
	#endregion

	#region Public methods
	public async Task<AuthSession?> LoadAsync()
	{
		try
		{
			string? raw = await SecureStorage.Default.GetAsync(SessionKey);

			if (string.IsNullOrEmpty(raw))
			{
				return null;
			}

			AuthSession? session = JsonSerializer.Deserialize<AuthSession>(raw, JsonOptions);

			if (session is null
				|| string.IsNullOrEmpty(session.AccessToken)
				|| string.IsNullOrEmpty(session.RefreshToken))
			{
				return null;
			}

			return session;
		}
		catch
		{
			// Any SecureStorage read or deserialization failure (platform keystore issue,
			// first-access quirk, layout change) is treated as "no session" — routes the caller
			// to Login rather than crashing.
			return null;
		}
	}

	public async Task SaveAsync(AuthSession session)
	{
		try
		{
			await SecureStorage.Default.SetAsync(SessionKey, JsonSerializer.Serialize(session, JsonOptions));
		}
		catch
		{
			// A failed write must not leave a stale session behind that a later launch would
			// restore; fall back to "no session".
			await ClearAsync();
		}
	}

	public Task ClearAsync()
	{
		SecureStorage.Default.Remove(SessionKey);
		return Task.CompletedTask;
	}
	#endregion
}
