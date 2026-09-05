using ChoNaBojo.App.ViewModels;
using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.App.Views;

public partial class EventDetailPage : ContentPage
{
	#region Private fields
	private readonly EventDetailViewModel _viewModel;
	private TaskCompletionSource<bool>? _completion;
	#endregion

	#region Constructors
	public EventDetailPage(EventDetailViewModel viewModel)
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
		_viewModel.CloseRequested += OnCloseRequested;
	}

	protected override void OnDisappearing()
	{
		_viewModel.CloseRequested -= OnCloseRequested;

		TaskCompletionSource<bool>? completion = _completion;
		_completion = null;
		completion?.TrySetResult(true);
		base.OnDisappearing();
	}
	#endregion

	#region Public methods
	public async Task ShowAsync(
		INavigation navigation,
		CreatedEventResponse createdEvent)
	{
		if (_completion is not null)
		{
			throw new InvalidOperationException("The event detail page is already open.");
		}

		_completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_viewModel.Prepare(createdEvent);
		await navigation.PushModalAsync(new NavigationPage(this));
		await _completion.Task.ConfigureAwait(true);
	}
	#endregion

	#region Event handlers
	private async void OnCloseRequested(object? sender, EventArgs e)
	{
		TaskCompletionSource<bool>? completion = _completion;
		if (completion is null)
		{
			return;
		}

		_completion = null;
		_viewModel.CloseRequested -= OnCloseRequested;
		await Navigation.PopModalAsync();
		completion.TrySetResult(true);
	}
	#endregion
}
