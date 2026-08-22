using ChoNaBojo.App.ViewModels;

namespace ChoNaBojo.App.Views;

public partial class HomePage : ContentPage
{
	#region Private fields
	private readonly HomeViewModel _viewModel;
	#endregion

	#region Constructors
	public HomePage(HomeViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = _viewModel;
	}
	#endregion

	#region Overrides
	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.AppearingCommand.Execute(null);
	}
	#endregion
}
