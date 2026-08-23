namespace ChoNaBojo.App.Services.Auth;

/// <summary>
/// Depends only on <see cref="ITokenStore"/> and <see cref="IAuthTokenClient"/> — never on
/// <see cref="IApiService"/> — so it can be safely consumed by
/// <see cref="AuthenticatingHttpMessageHandler"/> without recursing back into the handled
/// "ChoNaBojoApi" client.
/// </summary>
public class SessionService: ISessionService
{
	#region Private fields
	private readonly ITokenStore _tokenStore;
	private readonly IAuthTokenClient _authTokenClient;
	private readonly SemaphoreSlim _signOutLock = new(1, 1);
	private bool _signedOut = true;
	#endregion

	#region Constructors
	public SessionService(ITokenStore tokenStore, IAuthTokenClient authTokenClient)
	{
		_tokenStore = tokenStore;
		_authTokenClient = authTokenClient;
	}
	#endregion

	#region Public properties
	public AuthSession? Current
	{
		get; private set;
	}

	public bool IsAuthenticated
	{
		get
		{
			return Current is not null;
		}
	}
	#endregion

	#region Public events
	public event EventHandler? SessionExpired;
	#endregion

	#region Public methods
	public async Task InitializeAsync()
	{
		Current = await _tokenStore.LoadAsync();
		_signedOut = Current is null;
	}

	public async Task SetAsync(AuthSession session)
	{
		await _signOutLock.WaitAsync();
		try
		{
			// An explicit sign-in always wins — it clears any prior signed-out state.
			await _tokenStore.SaveAsync(session);
			Current = session;
			_signedOut = false;
		}
		finally
		{
			_signOutLock.Release();
		}
	}

	public async Task<bool> TryRenewAsync(AuthSession session)
	{
		await _signOutLock.WaitAsync();
		try
		{
			if (_signedOut)
			{
				// Sign-out won the race: a refresh landing afterwards is stale and must not
				// re-persist tokens or resurrect Current.
				return false;
			}

			await _tokenStore.SaveAsync(session);
			Current = session;
			return true;
		}
		finally
		{
			_signOutLock.Release();
		}
	}

	public async Task SignOutAsync(bool revokeServer)
	{
		string? refreshTokenToRevoke = null;

		await _signOutLock.WaitAsync();
		try
		{
			if (_signedOut)
			{
				// Already signed out by a concurrent caller — no-op keeps this idempotent.
				return;
			}

			_signedOut = true;
			refreshTokenToRevoke = Current?.RefreshToken;
			Current = null;
			await _tokenStore.ClearAsync();
		}
		finally
		{
			_signOutLock.Release();
		}

		if (revokeServer && !string.IsNullOrEmpty(refreshTokenToRevoke))
		{
			try
			{
				await _authTokenClient.LogoutAsync(refreshTokenToRevoke, CancellationToken.None);
			}
			catch
			{
				// Best-effort server-side revocation only; local sign-out has already happened.
			}
		}

		if (!revokeServer)
		{
			SessionExpired?.Invoke(this, EventArgs.Empty);
		}
	}
	#endregion
}
