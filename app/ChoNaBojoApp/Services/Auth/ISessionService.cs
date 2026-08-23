namespace ChoNaBojo.App.Services.Auth;

/// <summary>
/// Single source of truth for "am I signed in". Exposes the current tokens to the
/// authenticating handler and raises <see cref="SessionExpired"/> when a session ends
/// unexpectedly (refresh failure), as opposed to an explicit user-initiated logout.
/// </summary>
public interface ISessionService
{
	AuthSession? Current { get; }
	bool IsAuthenticated { get; }

	/// <summary>Loads any persisted session from <see cref="ITokenStore"/> on startup.</summary>
	Task InitializeAsync();

	/// <summary>Persists and adopts a newly issued session from an explicit sign-in (login/register).</summary>
	Task SetAsync(AuthSession session);

	/// <summary>
	/// Persists and adopts a session issued by a transparent refresh. Returns <c>false</c> and
	/// discards it when a sign-out completed while the refresh was in flight — such a refresh is
	/// stale by definition and must never resurrect the session.
	/// </summary>
	Task<bool> TryRenewAsync(AuthSession session);

	/// <summary>
	/// Clears the local session. When <paramref name="revokeServer"/> is <c>true</c> (explicit
	/// user logout) the refresh-token family is revoked server-side and no event is raised —
	/// the caller navigates itself. When <c>false</c> (session ended unexpectedly, e.g. an
	/// unrecoverable refresh failure) <see cref="SessionExpired"/> fires so subscribers can
	/// route to Login. Idempotent: concurrent calls result in a single transition.
	/// </summary>
	Task SignOutAsync(bool revokeServer);

	event EventHandler? SessionExpired;
}
