using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChoNaBojo.App.Services;
using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Feedback;
using ChoNaBojo.App.Services.Navigation;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Validation;

namespace ChoNaBojo.App.ViewModels;

/// <summary>
/// Drives the Login form: shared-validation inline errors first, then the API call, mapping
/// every <see cref="AuthResult"/> outcome onto either the per-field errors or a snackbar.
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
	#region Private fields
	private readonly IApiService _apiService;
	private readonly ISessionService _sessionService;
	private readonly INavigationRootService _navigationRootService;
	private readonly IFeedbackService _feedbackService;
	#endregion

	#region Observable properties
	[ObservableProperty]
	private string loginEmail = string.Empty;

	[ObservableProperty]
	private string password = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasLoginEmailError))]
	private string? loginEmailError;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasPasswordError))]
	private string? passwordError;
	#endregion

	#region Events
	/// <summary>
	/// Raised on the main thread after <see cref="LoginAsync"/> has fully completed and the
	/// command has finished flushing its <c>CanExecuteChanged</c> notifications. The view owns
	/// the root-page swap so LoginPage is never torn down while its handlers are still in use.
	/// </summary>
	public event EventHandler? LoginSucceeded;
	#endregion

	#region Public properties
	public bool HasLoginEmailError
	{
		get
		{
			return !string.IsNullOrEmpty(LoginEmailError);
		}
	}

	public bool HasPasswordError
	{
		get
		{
			return !string.IsNullOrEmpty(PasswordError);
		}
	}
	#endregion

	#region Constructors
	public LoginViewModel(IApiService apiService, ISessionService sessionService, INavigationRootService navigationRootService, IFeedbackService feedbackService)
	{
		_apiService = apiService;
		_sessionService = sessionService;
		_navigationRootService = navigationRootService;
		_feedbackService = feedbackService;
	}
	#endregion

	#region Commands
	[RelayCommand]
	private async Task LoginAsync()
	{
		if (IsBusy)
		{
			return;
		}

		ClearErrors();
		IsBusy = true;
		bool navigateToApp = false;
		try
		{
			var request = new LoginRequest(LoginEmail, Password);
			ValidationResult validation = AuthValidation.ValidateLoginRequest(request);
			if (!validation.IsValid)
			{
				ApplyValidationErrors(validation.Errors);
				return;
			}

			AuthResult result = await _apiService.LoginAsync(request, CancellationToken.None);
			switch (result.Status)
			{
				case AuthResultStatus.Success:
					var session = new AuthSession(
						result.Response!.AccessToken,
						result.Response.RefreshToken,
						result.Response.AccessTokenExpiresUtc);
					await _sessionService.SetAsync(session);
					navigateToApp = true;
					break;

				case AuthResultStatus.ValidationFailed:
					ApplyValidationErrors(result.ValidationErrors!);
					break;

				case AuthResultStatus.Unauthorized:
					await ShowSnackbarAsync("Invalid email or password.");
					break;

				case AuthResultStatus.Network:
					await ShowSnackbarAsync("Can't reach the server. Please try again.");
					break;

				default:
					await ShowSnackbarAsync("Something went wrong. Please try again.");
					break;
			}
		}
		finally
		{
			IsBusy = false;
		}

		if (navigateToApp)
		{
			// Raised (not navigated) on purpose: the command is still completing here, so the
			// view defers the root-page swap until LoginCommand's task has finished. Swapping
			// now would tear LoginPage down before AsyncRelayCommand's final CanExecuteChanged
			// re-applies the login Button's IsEnabled binding.
			LoginSucceeded?.Invoke(this, EventArgs.Empty);
		}
	}

	[RelayCommand]
	private void GoToRegister()
	{
		// Wired to a RegisterPage push once it exists — see Phase 3 ("Register routing
		// wiring") of the account-and-session plan.
	}
	#endregion

	#region Private methods
	private void ApplyValidationErrors(IReadOnlyDictionary<string, string[]> errors)
	{
		if (errors.TryGetValue("loginEmail", out string[]? loginEmailErrors))
		{
			LoginEmailError = string.Join(" ", loginEmailErrors);
		}

		if (errors.TryGetValue("password", out string[]? passwordErrors))
		{
			PasswordError = string.Join(" ", passwordErrors);
		}
	}

	private void ClearErrors()
	{
		LoginEmailError = null;
		PasswordError = null;
	}

	private Task ShowSnackbarAsync(string message)
	{
		return _feedbackService.ShowSnackbarAsync(message);
	}
	#endregion
}
