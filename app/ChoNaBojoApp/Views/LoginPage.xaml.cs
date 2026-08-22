using ChoNaBojo.App.Services.Navigation;
using ChoNaBojo.App.ViewModels;

namespace ChoNaBojo.App.Views;

public partial class LoginPage : ContentPage
{
	#region Private fields
	private readonly INavigationRootService _navigationRootService;
	private readonly LoginViewModel _viewModel;
	#endregion

	#region Constructors
	public LoginPage(LoginViewModel viewModel, INavigationRootService navigationRootService)
	{
		InitializeComponent();
		BindingContext = viewModel;
		_navigationRootService = navigationRootService;
		_viewModel = viewModel;
	}
	#endregion

	#region Overrides
	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoginSucceeded += OnLoginSucceeded;
	}

	protected override void OnDisappearing()
	{
		_viewModel.LoginSucceeded -= OnLoginSucceeded;
		base.OnDisappearing();
	}
	#endregion

	#region Events handlers
	private async void OnLoginSucceeded(object? sender, EventArgs e)
	{
		// The event is raised while LoginCommand is still completing. Swapping the root here
		// would disconnect this page's handlers before AsyncRelayCommand raises its final
		// CanExecuteChanged, which re-applies the login Button's IsEnabled binding and would
		// hit a null PlatformView. Awaiting the command's task lets that notification run
		// first; this continuation is queued after the command's own one.
		Task? executionTask = _viewModel.LoginCommand.ExecutionTask;
		if (executionTask is not null)
		{
			await executionTask.ConfigureAwait(true);
		}

		_navigationRootService.SetAppRoot();
	}
	#endregion
}
