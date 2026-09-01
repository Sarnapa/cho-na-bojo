using ChoNaBojo.App.Services.Geocoding;
using ChoNaBojo.App.ViewModels;

namespace ChoNaBojo.App.Views;

public partial class AddressSearchPage : ContentPage
{
	#region Private fields
	private readonly AddressSearchViewModel _viewModel;
	private TaskCompletionSource<AddressSuggestion?>? _completion;
	#endregion

	#region Constructors
	public AddressSearchPage(AddressSearchViewModel viewModel)
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
		_viewModel.AddressSelected += OnAddressSelected;
		_viewModel.CancelRequested += OnCancelRequested;
		Dispatcher.Dispatch(() => AddressField.Focus());
	}

	protected override void OnDisappearing()
	{
		_viewModel.AddressSelected -= OnAddressSelected;
		_viewModel.CancelRequested -= OnCancelRequested;
		_viewModel.Stop();

		TaskCompletionSource<AddressSuggestion?>? completion = _completion;
		_completion = null;
		completion?.TrySetResult(null);
		base.OnDisappearing();
	}
	#endregion

	#region Public methods
	public async Task<AddressSuggestion?> ShowAsync(
		INavigation navigation,
		string initialQuery)
	{
		if (_completion is not null)
		{
			throw new InvalidOperationException("The address search page is already open.");
		}

		_completion = new(
			TaskCreationOptions.RunContinuationsAsynchronously);
		_viewModel.Prepare(initialQuery);
		await navigation.PushModalAsync(new NavigationPage(this));
		return await _completion.Task.ConfigureAwait(true);
	}
	#endregion

	#region Event handlers
	private async void OnAddressSelected(object? sender, AddressSuggestion suggestion)
	{
		TaskCompletionSource<AddressSuggestion?>? completion = _completion;
		_completion = null;
		await Navigation.PopModalAsync();
		completion?.TrySetResult(suggestion);
	}

	private async void OnCancelRequested(object? sender, EventArgs e)
	{
		TaskCompletionSource<AddressSuggestion?>? completion = _completion;
		_completion = null;
		await Navigation.PopModalAsync();
		completion?.TrySetResult(null);
	}
	#endregion
}
