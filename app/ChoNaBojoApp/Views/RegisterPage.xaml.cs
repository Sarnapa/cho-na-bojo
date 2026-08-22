using ChoNaBojo.App.Services.Navigation;
using ChoNaBojo.App.ViewModels;

namespace ChoNaBojo.App.Views;

public partial class RegisterPage : ContentPage
{
	#region Private fields
	private readonly INavigationRootService _navigationRootService;
	private readonly RegisterViewModel _viewModel;
	#endregion

	#region Constructors
	public RegisterPage(RegisterViewModel viewModel, INavigationRootService navigationRootService)
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
		_viewModel.RegisterSucceeded += OnRegisterSucceeded;
		_viewModel.GoToLoginRequested += OnGoToLoginRequested;
	}

	protected override void OnDisappearing()
	{
		_viewModel.RegisterSucceeded -= OnRegisterSucceeded;
		_viewModel.GoToLoginRequested -= OnGoToLoginRequested;
		base.OnDisappearing();
	}
	#endregion

	#region Events handlers
	private async void OnRegisterSucceeded(object? sender, EventArgs e)
	{
		// Same rationale as LoginPage.OnLoginSucceeded: let RegisterCommand's task fully
		// complete (including its final CanExecuteChanged) before the root swap tears this
		// page down.
		Task? executionTask = _viewModel.RegisterCommand.ExecutionTask;
		if (executionTask is not null)
		{
			await executionTask.ConfigureAwait(true);
		}

		_navigationRootService.SetAppRoot();
	}

	private async void OnGoToLoginRequested(object? sender, EventArgs e)
	{
		await Navigation.PopAsync();
	}
	#endregion
}
