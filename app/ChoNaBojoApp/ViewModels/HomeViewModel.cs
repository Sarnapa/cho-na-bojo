using CommunityToolkit.Mvvm.Input;
using ChoNaBojo.App.Services;
using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Feedback;

namespace ChoNaBojo.App.ViewModels;

/// <summary>
/// Minimal Home placeholder ViewModel. Its only job in this slice is to exercise the one
/// protected call (<c>GET /auth/me</c>) so bearer attach, transparent 401 refresh, and expiry
/// sign-out are all demonstrable — never blocking the UI on the result.
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
	#region Private fields
	private readonly IApiService _apiService;
	private readonly IFeedbackService _feedbackService;
	#endregion

	#region Constructors
	public HomeViewModel(IApiService apiService, IFeedbackService feedbackService)
	{
		_apiService = apiService;
		_feedbackService = feedbackService;
	}
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
	#endregion
}
