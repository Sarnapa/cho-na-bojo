using System.Net;
using System.Net.Http.Headers;

namespace ChoNaBojo.App.Services.Auth;

/// <summary>
/// Attaches the bearer to protected "ChoNaBojoApi" calls and refreshes transparently — either
/// pre-flight (access token within 60s of expiry) or reactively (on a 401) — retrying the
/// original request once. Depends only on <see cref="ITokenStore"/>/<see cref="ISessionService"/>
/// + <see cref="IAuthTokenClient"/>, never <see cref="IApiService"/>, so resolving
/// "ChoNaBojoApi" can never recurse back into this handler.
/// Single-flight refresh guarded by a semaphore; see plan Critical Implementation Details for
/// the change-detection + 409/401/transient classification this implements.
/// </summary>
public class AuthenticatingHttpMessageHandler : DelegatingHandler
{
	#region Private fields
	private static readonly string[] AnonymousAuthPaths =
	[
		"/auth/register",
		"/auth/login",
		"/auth/refresh",
		"/auth/logout"
	];

	private readonly ISessionService _sessionService;
	private readonly IAuthTokenClient _authTokenClient;
	private readonly SemaphoreSlim _refreshLock = new(1, 1);
	#endregion

	#region Constructors
	public AuthenticatingHttpMessageHandler(ISessionService sessionService, IAuthTokenClient authTokenClient)
	{
		_sessionService = sessionService;
		_authTokenClient = authTokenClient;
	}
	#endregion

	#region Overrides
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (IsAnonymousAuthRequest(request))
		{
			return await base.SendAsync(request, cancellationToken);
		}

		AuthSession? session = _sessionService.Current;
		if (session is not null && session.AccessTokenExpiresUtc <= DateTime.UtcNow.AddSeconds(60))
		{
			// Pre-flight expiry check: the 15-min access token TTL is enforced here, not just
			// reactively on 401 — this is the reason `auth_expires` is persisted.
			await RefreshAsync(cancellationToken);
			session = _sessionService.Current;
		}

		if (session is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
		}

		HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

		if (response.StatusCode != HttpStatusCode.Unauthorized || _sessionService.Current is null)
		{
			return response;
		}

		AuthSession? refreshed = await RefreshAsync(cancellationToken);
		if (refreshed is null)
		{
			return response;
		}

		response.Dispose();
		HttpRequestMessage retryRequest = await CloneRequestAsync(request);
		retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
		return await base.SendAsync(retryRequest, cancellationToken);
	}
	#endregion

	#region Private methods
	private async Task<AuthSession?> RefreshAsync(CancellationToken cancellationToken)
	{
		AuthSession? beforeWait = _sessionService.Current;

		await _refreshLock.WaitAsync(cancellationToken);
		try
		{
			AuthSession? current = _sessionService.Current;

			// Another waiter may have already refreshed while we queued for the lock.
			if (current is not null && beforeWait is not null && current.AccessToken != beforeWait.AccessToken)
			{
				return current;
			}

			if (current is null)
			{
				return null;
			}

			RefreshOutcome outcome = await _authTokenClient.RefreshAsync(current.RefreshToken, cancellationToken);

			switch (outcome.Status)
			{
				case RefreshOutcomeStatus.Success:
					var refreshedSession = new AuthSession(
						outcome.Response!.AccessToken,
						outcome.Response.RefreshToken,
						outcome.Response.AccessTokenExpiresUtc);
					await _sessionService.SetAsync(refreshedSession);
					return refreshedSession;

				case RefreshOutcomeStatus.RetryInProgress:
					// The server already consumed this refresh token and never returns the
					// replacement's raw value. Re-read the store in case another process
					// rotated it while we waited; otherwise the session is unrecoverable.
					AuthSession? afterConflict = _sessionService.Current;
					if (afterConflict is not null && afterConflict.AccessToken != current.AccessToken)
					{
						return afterConflict;
					}

					await _sessionService.SignOutAsync(revokeServer: false);
					return null;

				case RefreshOutcomeStatus.Invalid:
					await _sessionService.SignOutAsync(revokeServer: false);
					return null;

				case RefreshOutcomeStatus.Transient:
				default:
					// 429/5xx/transport failure — keep the session; the caller's original
					// request failure propagates to the caller as a network error.
					return null;
			}
		}
		finally
		{
			_refreshLock.Release();
		}
	}

	private static bool IsAnonymousAuthRequest(HttpRequestMessage request)
	{
		string path = request.RequestUri?.AbsolutePath ?? string.Empty;
		return AnonymousAuthPaths.Any(anonymousPath => path.EndsWith(anonymousPath, StringComparison.OrdinalIgnoreCase));
	}

	private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
	{
		var clone = new HttpRequestMessage(request.Method, request.RequestUri)
		{
			Version = request.Version
		};

		if (request.Content is not null)
		{
			byte[] buffer = await request.Content.ReadAsByteArrayAsync();
			clone.Content = new ByteArrayContent(buffer);
			foreach (var header in request.Content.Headers)
			{
				clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
			}
		}

		foreach (var header in request.Headers)
		{
			clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
		}

		foreach (var option in request.Options)
		{
			clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
		}

		return clone;
	}
	#endregion
}
