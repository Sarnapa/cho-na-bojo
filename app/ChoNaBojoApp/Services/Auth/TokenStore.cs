using System.Globalization;

namespace ChoNaBojo.App.Services.Auth;

/// <summary>
/// <see cref="SecureStorage"/>-backed implementation of <see cref="ITokenStore"/>. No secrets
/// are ever logged. See ui-guidelines/plan Critical Implementation Details for the Android
/// 10+ main-thread requirement on the first <see cref="SecureStorage"/> access — callers are
/// responsible for invoking this on the main thread during startup.
/// </summary>
public class TokenStore : ITokenStore
{
	#region Private fields
	private const string AccessTokenKey = "auth_access";
	private const string RefreshTokenKey = "auth_refresh";
	private const string ExpiresKey = "auth_expires";
	#endregion
	
	#region Public methods
	public async Task<AuthSession?> LoadAsync()
	{
		try
		{
			string? accessToken = await SecureStorage.Default.GetAsync(AccessTokenKey);
			string? refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
			string? expiresRaw = await SecureStorage.Default.GetAsync(ExpiresKey);

			if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(expiresRaw))
			{
				return null;
			}

			if (!DateTime.TryParse(expiresRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime expiresUtc))
			{
				return null;
			}

			return new AuthSession(accessToken, refreshToken, expiresUtc);
		}
		catch
		{
			// Any SecureStorage read failure (platform keystore issue, first-access quirk, etc.)
			// is treated as "no session" — routes the caller to Login rather than crashing.
			return null;
		}
	}

	public async Task SaveAsync(AuthSession session)
	{
		await SecureStorage.Default.SetAsync(AccessTokenKey, session.AccessToken);
		await SecureStorage.Default.SetAsync(RefreshTokenKey, session.RefreshToken);
		await SecureStorage.Default.SetAsync(ExpiresKey, session.AccessTokenExpiresUtc.ToString("o", CultureInfo.InvariantCulture));
	}

	public Task ClearAsync()
	{
		SecureStorage.Default.Remove(AccessTokenKey);
		SecureStorage.Default.Remove(RefreshTokenKey);
		SecureStorage.Default.Remove(ExpiresKey);
		return Task.CompletedTask;
	}
	#endregion
}
