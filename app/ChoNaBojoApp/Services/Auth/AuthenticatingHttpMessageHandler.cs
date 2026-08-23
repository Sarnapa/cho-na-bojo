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

	// Static on purpose: the handler is transient and IHttpClientFactory rotates handler chains,
	// but the resource this guards — the single stored refresh token — is process-global, so the
	// single-flight guarantee must be process-global too.
	private static readonly SemaphoreSlim _refreshLock = new(1, 1);
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

		// Buffer the body up-front: HttpClient disposes the request content once the response is
		// received, so a retry clone built afterwards could not re-read it.
		byte[]? bufferedContent = null;
		List<KeyValuePair<string, IEnumerable<string>>>? bufferedContentHeaders = null;
		if (request.Content is not null)
		{
			bufferedContent = await request.Content.ReadAsByteArrayAsync(cancellationToken);
			bufferedContentHeaders = request.Content.Headers.ToList();
		}

		AuthSession? session = _sessionService.Current;
		if (session is not null && session.AccessTokenExpiresUtc <= DateTime.UtcNow.AddSeconds(60))
		{
			// Pre-flight expiry check: the 15-min access token TTL is enforced here, not just
			// reactively on 401 — this is the reason `auth_expires` is persisted. A transient
			// failure here is deliberately ignored: the current token may still be valid for a
			// few more seconds, and if it is not the reactive path below reports the failure.
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

		RefreshResult refreshResult = await RefreshAsync(cancellationToken);
		if (refreshResult.Session is null)
		{
			if (refreshResult.IsTransient)
			{
				// 429/5xx/transport: the session is intentionally preserved, but the caller must
				// see a network failure (retry snackbar) rather than a silent 401.
				response.Dispose();
				throw new HttpRequestException(
					"Access token refresh failed transiently (429/5xx/transport). Session preserved; caller should offer a retry.");
			}

			return response;
		}

		response.Dispose();
		HttpRequestMessage retryRequest = CloneRequest(request, bufferedContent, bufferedContentHeaders);
		retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshResult.Session.AccessToken);
		return await base.SendAsync(retryRequest, cancellationToken);
	}
	#endregion

	#region Private types
	private readonly record struct RefreshResult(AuthSession? Session, bool IsTransient)
	{
		public static readonly RefreshResult Failed = new(null, false);
		public static readonly RefreshResult TransientFailure = new(null, true);

		public static RefreshResult Renewed(AuthSession session)
		{
			return new RefreshResult(session, false);
		}
	}
	#endregion

	#region Private methods
	private async Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken)
	{
		AuthSession? beforeWait = _sessionService.Current;

		await _refreshLock.WaitAsync(cancellationToken);
		try
		{
			AuthSession? current = _sessionService.Current;

			// Another waiter may have already refreshed while we queued for the lock.
			if (current is not null && beforeWait is not null && current.AccessToken != beforeWait.AccessToken)
			{
				return RefreshResult.Renewed(current);
			}

			if (current is null)
			{
				return RefreshResult.Failed;
			}

			RefreshOutcome outcome = await _authTokenClient.RefreshAsync(current.RefreshToken, cancellationToken);

			switch (outcome.Status)
			{
				case RefreshOutcomeStatus.Success:
					var refreshedSession = new AuthSession(
						outcome.Response!.AccessToken,
						outcome.Response.RefreshToken,
						outcome.Response.AccessTokenExpiresUtc);
					if (!await _sessionService.TryRenewAsync(refreshedSession))
					{
						// Signed out while this refresh was in flight — treat as no session.
						return RefreshResult.Failed;
					}

					return RefreshResult.Renewed(refreshedSession);

				case RefreshOutcomeStatus.RetryInProgress:
					// The server already consumed this refresh token and never returns the
					// replacement's raw value. Re-read the store in case another process
					// rotated it while we waited; otherwise the session is unrecoverable.
					AuthSession? afterConflict = _sessionService.Current;
					if (afterConflict is not null && afterConflict.AccessToken != current.AccessToken)
					{
						return RefreshResult.Renewed(afterConflict);
					}

					await _sessionService.SignOutAsync(revokeServer: false);
					return RefreshResult.Failed;

				case RefreshOutcomeStatus.Invalid:
					await _sessionService.SignOutAsync(revokeServer: false);
					return RefreshResult.Failed;

				case RefreshOutcomeStatus.Transient:
				default:
					// 429/5xx/transport failure — keep the session; the caller's original
					// request failure propagates to the caller as a network error.
					return RefreshResult.TransientFailure;
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

	private static HttpRequestMessage CloneRequest(
		HttpRequestMessage request,
		byte[]? bufferedContent,
		List<KeyValuePair<string, IEnumerable<string>>>? bufferedContentHeaders)
	{
		var clone = new HttpRequestMessage(request.Method, request.RequestUri)
		{
			Version = request.Version
		};

		if (bufferedContent is not null)
		{
			clone.Content = new ByteArrayContent(bufferedContent);
			foreach (var header in bufferedContentHeaders ?? [])
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
