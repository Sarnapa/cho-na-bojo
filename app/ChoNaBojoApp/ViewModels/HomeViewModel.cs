using CommunityToolkit.Mvvm.Input;
using ChoNaBojo.App.Services;
using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Feedback;

namespace ChoNaBojo.App.ViewModels;

/// <summary>
/// Minimal Home placeholder ViewModel. Its only job in this slice is to exercise the one
/// protected call (<c>GET /auth/me</c>) so bearer attach, transparent 401 refresh, and expiry
/// sign-out are all demonstrable — never blocking the UI on the result. Also owns the explicit
/// logout flow (Phase 4).
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
	#region Private fields
	private readonly IApiService _apiService;
	private readonly ISessionService _sessionService;
	private readonly IFeedbackService _feedbackService;
	#endregion

	#region Constructors
	public HomeViewModel(IApiService apiService, ISessionService sessionService, IFeedbackService feedbackService)
	{
		_apiService = apiService;
		_sessionService = sessionService;
		_feedbackService = feedbackService;
	}
	#endregion

	#region Events
	/// <summary>
	/// Raised on the main thread after <see cref="LogoutAsync"/> has fully completed and the
	/// command has finished flushing its <c>CanExecuteChanged</c> notifications. Mirrors
	/// LoginViewModel.LoginSucceeded: the view owns the root-page swap so HomePage is never
	/// torn down while its Logout button's IsEnabled binding is still being re-applied.
	/// </summary>
	public event EventHandler? LoggedOut;
	#endregion

	#region Commands
	[RelayCommand]
	private async Task AppearingAsync()
	{
		CurrentUserResult result = await _apiService.GetCurrentUserAsync(CancellationToken.None);

		// Success: silent, Home renders regardless. Unauthorized: handled by
		// AuthenticatingHttpMessageHandler's expiry path (SessionExpired), not here.
		if (result.Status == CurrentUserResultStatus.Network)
		{
			await _feedbackService.ShowSnackbarAsync("Can't reach the server. Please check your connection.");
		}
	}

	[RelayCommand]
	private async Task LogoutAsync()
	{
		if (IsBusy)
		{
			return;
		}

		bool confirmed = await _feedbackService.ShowConfirmAsync(
			title: "Log out?",
			message: "You'll need to sign in again to continue.",
			confirmText: "Log out",
			cancelText: "Cancel");
		if (!confirmed)
		{
			return;
		}

		IsBusy = true;
		try
		{
			await _sessionService.SignOutAsync(revokeServer: true);
		}
		finally
		{
			IsBusy = false;
		}

		LoggedOut?.Invoke(this, EventArgs.Empty);
	}
	#endregion
}
