using ChoNaBojo.App.ViewModels;
using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.App.Views;

#region CreateEventPageResult
public enum CreateEventPageResultStatus
{
	Cancelled,
	Created,
	VenueInvalidated
}

public sealed record CreateEventPageResult(
	CreateEventPageResultStatus Status,
	CreatedEventResponse? CreatedEvent,
	bool IsReplay,
	bool CatalogWasRefreshed,
	string? Message);
#endregion

public partial class CreateEventPage : ContentPage
{
	#region Private fields
	private readonly CreateEventViewModel _viewModel;
	private TaskCompletionSource<CreateEventPageResult>? _completion;
	private bool _dismissedWhileInFlight;
	#endregion

	#region Constructors
	public CreateEventPage(CreateEventViewModel viewModel)
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
		AttachHandlers();
	}

	protected override void OnDisappearing()
	{
		if (_completion is not null)
		{
			if (_viewModel.IsRequestInFlight)
			{
				// A host-level dismissal bypassed the Back guard. Keep observing only until the
				// request settles, then release the map without surfacing a result through this
				// page, which is no longer alive.
				_dismissedWhileInFlight = true;
			}
			else
			{
				CompleteWithoutNavigation(CreateCancelledResult());
			}
		}

		base.OnDisappearing();
	}

	protected override bool OnBackButtonPressed()
	{
		return _viewModel.IsRequestInFlight || base.OnBackButtonPressed();
	}
	#endregion

	#region Public methods
	public async Task<CreateEventPageResult> ShowAsync(
		INavigation navigation,
		VenueResponse venue,
		int? activeSportId)
	{
		if (_completion is not null)
		{
			throw new InvalidOperationException("The create event page is already open.");
		}

		_dismissedWhileInFlight = false;
		_completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_viewModel.Prepare(venue, activeSportId);
		await navigation.PushModalAsync(new NavigationPage(this));
		return await _completion.Task.ConfigureAwait(true);
	}
	#endregion

	#region Event handlers
	private async void OnEventCreated(object? sender, EventCreatedEventArgs e)
	{
		if (_dismissedWhileInFlight)
		{
			return;
		}

		await CompleteAndCloseAsync(new CreateEventPageResult(
			CreateEventPageResultStatus.Created,
			e.Response,
			e.IsReplay,
			_viewModel.CatalogWasRefreshed,
			null));
	}

	private async void OnCancelRequested(object? sender, EventArgs e)
	{
		await CompleteAndCloseAsync(CreateCancelledResult());
	}

	private void OnSportSelectionRequested(object? sender, EventArgs e)
	{
		if (_dismissedWhileInFlight)
		{
			return;
		}

		Dispatcher.Dispatch(() => SportField.Focus());
	}

	private async void OnVenueInvalidated(object? sender, VenueInvalidatedEventArgs e)
	{
		if (_dismissedWhileInFlight)
		{
			return;
		}

		await CompleteAndCloseAsync(new CreateEventPageResult(
			CreateEventPageResultStatus.VenueInvalidated,
			null,
			false,
			_viewModel.CatalogWasRefreshed,
			e.Message));
	}

	private void OnRequestSettled(object? sender, EventArgs e)
	{
		if (_dismissedWhileInFlight)
		{
			CompleteWithoutNavigation(CreateCancelledResult());
		}
	}
	#endregion

	#region Private methods
	private void AttachHandlers()
	{
		DetachHandlers();
		_viewModel.EventCreated += OnEventCreated;
		_viewModel.CancelRequested += OnCancelRequested;
		_viewModel.SportSelectionRequested += OnSportSelectionRequested;
		_viewModel.VenueInvalidated += OnVenueInvalidated;
		_viewModel.RequestSettled += OnRequestSettled;
	}

	private void DetachHandlers()
	{
		_viewModel.EventCreated -= OnEventCreated;
		_viewModel.CancelRequested -= OnCancelRequested;
		_viewModel.SportSelectionRequested -= OnSportSelectionRequested;
		_viewModel.VenueInvalidated -= OnVenueInvalidated;
		_viewModel.RequestSettled -= OnRequestSettled;
	}

	private CreateEventPageResult CreateCancelledResult()
	{
		return new CreateEventPageResult(
			CreateEventPageResultStatus.Cancelled,
			null,
			false,
			_viewModel.CatalogWasRefreshed,
			null);
	}

	private async Task CompleteAndCloseAsync(CreateEventPageResult result)
	{
		TaskCompletionSource<CreateEventPageResult>? completion = _completion;
		if (completion is null)
		{
			return;
		}

		_completion = null;
		DetachHandlers();
		await Navigation.PopModalAsync();
		completion.TrySetResult(result);
	}

	private void CompleteWithoutNavigation(CreateEventPageResult result)
	{
		TaskCompletionSource<CreateEventPageResult>? completion = _completion;
		if (completion is null)
		{
			return;
		}

		_completion = null;
		DetachHandlers();
		completion.TrySetResult(result);
	}
	#endregion
}
