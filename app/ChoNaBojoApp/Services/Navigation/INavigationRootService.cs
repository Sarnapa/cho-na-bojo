namespace ChoNaBojo.App.Services.Navigation;

/// <summary>
/// Centralizes swapping the window root between the unauthenticated Auth flow and the
/// authenticated App Shell, so login/register success, logout, and session expiry all share
/// one mechanism. Always replaces the root outright (never pushes) so a logged-out user can
/// never navigate back into app content — the PRD's core privacy boundary.
/// </summary>
public interface INavigationRootService
{
	void SetAuthRoot();

	void SetAppRoot();
}
