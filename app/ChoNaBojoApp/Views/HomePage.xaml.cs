using ChoNaBojo.App.Services.Navigation;
using ChoNaBojo.App.ViewModels;

namespace ChoNaBojo.App.Views;

public partial class HomePage : ContentPage
{
	#region Private fields
	private readonly INavigationRootService _navigationRootService;
	private readonly HomeViewModel _viewModel;
	#endregion

	#region Constructors
	public HomePage(HomeViewModel viewModel, INavigationRootService navigationRootService)
	{
		InitializeComponent();
		_viewModel = viewModel;
		_navigationRootService = navigationRootService;
		BindingContext = _viewModel;
	}
	#endregion

	#region Overrides
	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoggedOut += OnLoggedOut;
		_viewModel.AppearingCommand.Execute(null);
	}

	protected override void OnDisappearing()
	{
		_viewModel.LoggedOut -= OnLoggedOut;
		base.OnDisappearing();
	}
	#endregion

	#region Events handlers
	private async void OnLoggedOut(object? sender, EventArgs e)
	{
		// Mirrors LoginPage.OnLoginSucceeded: await the command's own task first so its final
		// CanExecuteChanged (re-applying the Logout button's IsEnabled binding) runs before this
		// page is torn down by the root swap — swapping first would hit a null PlatformView.
		Task? executionTask = _viewModel.LogoutCommand.ExecutionTask;
		if (executionTask is not null)
		{
			await executionTask.ConfigureAwait(true);
		}

		_navigationRootService.SetAuthRoot();
	}
	#endregion
}
