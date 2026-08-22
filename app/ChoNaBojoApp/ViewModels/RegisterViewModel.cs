using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChoNaBojo.App.Services;
using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Feedback;
using ChoNaBojo.App.Services.Navigation;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Contracts.Enums;
using ChoNaBojo.Validation;

namespace ChoNaBojo.App.ViewModels;

/// <summary>
/// Drives the Register form: shared-validation inline/group errors first (the "at least one
/// contact" rule and the paired communicator platform+handle rule), then the API call,
/// auto-logging the new user in on success.
/// </summary>
public partial class RegisterViewModel : ViewModelBase
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
	private string contactPhone = string.Empty;

	[ObservableProperty]
	private string contactEmail = string.Empty;

	[ObservableProperty]
	private CommunicatorPlatform? selectedCommunicatorPlatform;

	[ObservableProperty]
	private string communicatorLogin = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasLoginEmailError))]
	private string? loginEmailError;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasPasswordError))]
	private string? passwordError;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasContactError))]
	private string? contactError;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasCommunicatorError))]
	private string? communicatorError;
	#endregion

	#region Events
	/// <summary>
	/// Raised on the main thread after <see cref="RegisterAsync"/> has fully completed and the
	/// command has finished flushing its <c>CanExecuteChanged</c> notifications — mirrors
	/// <c>LoginViewModel.LoginSucceeded</c> so the view can safely swap the root afterwards.
	/// </summary>
	public event EventHandler? RegisterSucceeded;

	/// <summary>Raised when the user asks to go back to Login; the view pops the page.</summary>
	public event EventHandler? GoToLoginRequested;
	#endregion

	#region Public properties
	public IReadOnlyList<CommunicatorPlatform> CommunicatorPlatforms { get; } = Enum.GetValues<CommunicatorPlatform>();

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

	public bool HasContactError
	{
		get
		{
			return !string.IsNullOrEmpty(ContactError);
		}
	}

	public bool HasCommunicatorError
	{
		get
		{
			return !string.IsNullOrEmpty(CommunicatorError);
		}
	}
	#endregion

	#region Constructors
	public RegisterViewModel(IApiService apiService, ISessionService sessionService, INavigationRootService navigationRootService, IFeedbackService feedbackService)
	{
		_apiService = apiService;
		_sessionService = sessionService;
		_navigationRootService = navigationRootService;
		_feedbackService = feedbackService;
	}
	#endregion

	#region Commands
	[RelayCommand]
	private async Task RegisterAsync()
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
			var request = new RegisterRequest(
				LoginEmail,
				Password,
				NullIfEmpty(ContactPhone),
				NullIfEmpty(ContactEmail),
				SelectedCommunicatorPlatform,
				NullIfEmpty(CommunicatorLogin));

			ValidationResult validation = AuthValidation.ValidateRegisterRequest(request);
			if (!validation.IsValid)
			{
				ApplyValidationErrors(validation.Errors);
				return;
			}

			AuthResult result = await _apiService.RegisterAsync(request, CancellationToken.None);
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

				case AuthResultStatus.Conflict:
					LoginEmailError = "An account with this email already exists.";
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
			// Raised (not navigated) on purpose — see LoginViewModel.LoginSucceeded for the
			// full rationale: swapping the root here would tear this page down before
			// AsyncRelayCommand's final CanExecuteChanged re-applies bindings on it.
			RegisterSucceeded?.Invoke(this, EventArgs.Empty);
		}
	}

	[RelayCommand]
	private void GoToLogin()
	{
		GoToLoginRequested?.Invoke(this, EventArgs.Empty);
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

		if (errors.TryGetValue("contact", out string[]? contactErrors))
		{
			ContactError = string.Join(" ", contactErrors);
		}

		// "communicator" (paired platform+login) and "communicatorPlatform" (unsupported
		// value) both describe the same communicator row, so they render as one group error.
		var communicatorErrors = new List<string>();
		if (errors.TryGetValue("communicator", out string[]? communicatorGroupErrors))
		{
			communicatorErrors.AddRange(communicatorGroupErrors);
		}

		if (errors.TryGetValue("communicatorPlatform", out string[]? communicatorPlatformErrors))
		{
			communicatorErrors.AddRange(communicatorPlatformErrors);
		}

		if (communicatorErrors.Count > 0)
		{
			CommunicatorError = string.Join(" ", communicatorErrors);
		}
	}

	private void ClearErrors()
	{
		LoginEmailError = null;
		PasswordError = null;
		ContactError = null;
		CommunicatorError = null;
	}

	private static string? NullIfEmpty(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

	private Task ShowSnackbarAsync(string message)
	{
		return _feedbackService.ShowSnackbarAsync(message);
	}
	#endregion
}
