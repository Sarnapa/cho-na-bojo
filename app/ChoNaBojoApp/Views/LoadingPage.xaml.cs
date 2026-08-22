using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Navigation;

namespace ChoNaBojo.App.Views;

public partial class LoadingPage : ContentPage
{
	#region Private fields
	private readonly ISessionService _sessionService;
	private readonly INavigationRootService _navigationRootService;
	#endregion

	#region Constructors
	public LoadingPage(ISessionService sessionService, INavigationRootService navigationRootService)
	{
		InitializeComponent();
		_sessionService = sessionService;
		_navigationRootService = navigationRootService;
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
		}
		else
		{
			_navigationRootService.SetAuthRoot();
		}
	}
	#endregion
}
