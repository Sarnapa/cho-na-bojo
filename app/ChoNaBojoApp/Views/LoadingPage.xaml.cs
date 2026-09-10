using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Navigation;
using ChoNaBojo.App.Services.Push;

namespace ChoNaBojo.App.Views;

public partial class LoadingPage : ContentPage
{
	#region Private fields
	private readonly ISessionService _sessionService;
	private readonly INavigationRootService _navigationRootService;
	private readonly IPushRegistrationService _pushRegistrationService;
	#endregion

	#region Constructors
	public LoadingPage(
		ISessionService sessionService,
		INavigationRootService navigationRootService,
		IPushRegistrationService pushRegistrationService)
	{
		InitializeComponent();
		_sessionService = sessionService;
		_navigationRootService = navigationRootService;
		_pushRegistrationService = pushRegistrationService;
		Loaded += OnLoaded;
	}
	#endregion

	#region Private methods
	private async void OnLoaded(object? sender, EventArgs e)
	{
		Loaded -= OnLoaded;

		try
		{
			// Main-thread SecureStorage read — required on Android 10+ for the first access.
			await _sessionService.InitializeAsync();
		}
		catch
		{
			// Any unexpected startup failure routes to Login rather than crashing; TokenStore
			// itself already treats read failures as "no session".
			_navigationRootService.SetAuthRoot();
			return;
		}

		if (_sessionService.IsAuthenticated)
		{
			_navigationRootService.SetAppRoot();
			_ = _pushRegistrationService.SyncAsync(CancellationToken.None);
		}
		else
		{
			_navigationRootService.SetAuthRoot();
		}
	}
	#endregion
}
