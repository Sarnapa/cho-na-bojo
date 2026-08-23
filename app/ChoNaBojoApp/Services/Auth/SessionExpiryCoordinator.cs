using ChoNaBojo.App.Services.Feedback;
using ChoNaBojo.App.Services.Navigation;

namespace ChoNaBojo.App.Services.Auth;

/// <summary>
/// Subscribes to <see cref="ISessionService.SessionExpired"/> for the app's lifetime and routes
/// to Login with an explanatory snackbar. A dedicated singleton (rather than wiring the
/// subscription inline in <c>App.xaml.cs</c>) keeps <c>App</c> minimal; it is resolved eagerly
/// right after the DI container is built (see <c>MauiProgram</c>) so the subscription is live
/// before any page appears. Unlike the Home logout flow, this isn't tied to a specific page's
/// in-flight command, so no defer-to-view dance is needed — <see cref="SessionService"/> already
/// guarantees the event fires at most once per sign-out (see its <c>_signOutLock</c>).
/// </summary>
public class SessionExpiryCoordinator
{
	#region Private fields
	private readonly INavigationRootService _navigationRootService;
	private readonly IFeedbackService _feedbackService;
	#endregion

	#region Constructors
	public SessionExpiryCoordinator(ISessionService sessionService, INavigationRootService navigationRootService, IFeedbackService feedbackService)
	{
		_navigationRootService = navigationRootService;
		_feedbackService = feedbackService;
		sessionService.SessionExpired += OnSessionExpired;
	}
	#endregion

	#region Private methods
	private void OnSessionExpired(object? sender, EventArgs e)
	{
		// The refresh handler that raises this event runs off the main thread; marshal before
		// touching the window root or showing UI feedback.
		MainThread.BeginInvokeOnMainThread(async () =>
		{
			_navigationRootService.SetAuthRoot();
			await _feedbackService.ShowSnackbarAsync("Your session expired — please sign in.");
		});
	}
	#endregion
}
