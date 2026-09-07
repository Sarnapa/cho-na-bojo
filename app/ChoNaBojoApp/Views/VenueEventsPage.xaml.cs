using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ChoNaBojo.App.Services.Events;
using ChoNaBojo.App.ViewModels;

namespace ChoNaBojo.App.Views;

public partial class VenueEventsPage : ContentPage
{
	#region Private fields
	private readonly IServiceProvider _serviceProvider;
	private MapViewModel? _viewModel;
	private TaskCompletionSource? _completion;
	private bool _isOpeningChildModal;
	#endregion

	#region Public properties
	public AvailabilityFilterViewModel AvailabilityFilterViewModel { get; }
	#endregion

	#region Constructors
	public VenueEventsPage(
		IServiceProvider serviceProvider,
		AvailabilityFilterViewModel availabilityFilterViewModel)
	{
		AvailabilityFilterViewModel = availabilityFilterViewModel;
		InitializeComponent();
		_serviceProvider = serviceProvider;
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
		DetachHandlers();
		if (!_isOpeningChildModal)
		{
			CompleteWithoutNavigation();
		}

		base.OnDisappearing();
	}
	#endregion

	#region Public methods
	public async Task ShowAsync(
		INavigation navigation,
		MapViewModel viewModel,
		int venueId)
	{
		if (_completion is not null)
		{
			throw new InvalidOperationException("The venue events page is already open.");
		}

		_viewModel = viewModel;
		BindingContext = viewModel;
		_completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		viewModel.SelectVenue(venueId);
		await navigation.PushModalAsync(new NavigationPage(this));
		await _completion.Task.ConfigureAwait(true);
	}
	#endregion

	#region Event handlers
	private async void OnCloseClicked(object? sender, EventArgs e)
	{
		await CompleteAndCloseAsync();
	}

	private void OnFilterButtonLoaded(object? sender, EventArgs e)
	{
#if ANDROID
		if (sender is Button
			{
				Handler.PlatformView: Android.Widget.TextView nativeButton
			}
			&& OperatingSystem.IsAndroidVersionAtLeast(26))
		{
			nativeButton.SetMaxLines(2);
			nativeButton.SetAutoSizeTextTypeUniformWithConfiguration(
				12,
				16,
				1,
				(int)Android.Util.ComplexUnitType.Sp);
		}
#endif
	}

	private async void OnCreateEventRequested(
		object? sender,
		CreateEventRequestedEventArgs e)
	{
		if (_isOpeningChildModal || _viewModel is null)
		{
			return;
		}

		_isOpeningChildModal = true;
		try
		{
			CreateEventPage createPage =
				_serviceProvider.GetRequiredService<CreateEventPage>();
			CreateEventPageResult result = await createPage.ShowAsync(
				Navigation,
				e.Venue,
				e.ActiveSportId);

			if (result.CatalogWasRefreshed
				|| result.Status == CreateEventPageResultStatus.VenueInvalidated)
			{
				_viewModel.ApplyRefreshedCatalog(
					result.Status == CreateEventPageResultStatus.VenueInvalidated);
			}

			if (!string.IsNullOrEmpty(result.Message))
			{
				await _viewModel.ShowCreateFeedbackAsync(result.Message);
			}

			if (result.Status == CreateEventPageResultStatus.VenueInvalidated)
			{
				await CompleteAndCloseAsync();
			}
			else if (result is
				{
					Status: CreateEventPageResultStatus.Created,
					CreatedEvent: not null
				})
			{
				int createdVenueId = result.CreatedEvent.Venue.Id;
				EventDetailPage detailPage =
					_serviceProvider.GetRequiredService<EventDetailPage>();
				await detailPage.ShowAsync(Navigation, result.CreatedEvent);
				await _viewModel.ReloadSelectedVenueEventsAsync(createdVenueId);
			}
		}
		finally
		{
			_isOpeningChildModal = false;
		}
	}

	private void OnCustomAvailabilityRequested(object? sender, EventArgs e)
	{
		if (_viewModel is null)
		{
			return;
		}

		AvailabilityFilterViewModel.Prepare(
			_viewModel.CustomAvailabilityWindow);
	}

	private async void OnAvailabilityApplyRequested(
		object? sender,
		EventAvailabilityWindow window)
	{
		if (_viewModel is not null)
		{
			await _viewModel.ApplyCustomAvailabilityAsync(window);
		}
	}

	private async void OnAvailabilityClearRequested(object? sender, EventArgs e)
	{
		if (_viewModel is not null)
		{
			await _viewModel.ClearAvailabilityAsync();
		}
	}

	private void OnAvailabilityCancelRequested(object? sender, EventArgs e)
	{
		if (_viewModel is not null)
		{
			_viewModel.IsCustomAvailabilityEditorVisible = false;
		}
	}
	#endregion

	#region Private methods
	private void AttachHandlers()
	{
		if (_viewModel is null)
		{
			return;
		}

		DetachHandlers();
		_viewModel.CreateEventRequested += OnCreateEventRequested;
		_viewModel.CustomAvailabilityRequested += OnCustomAvailabilityRequested;
		_viewModel.PropertyChanged += OnViewModelPropertyChanged;
		AvailabilityFilterViewModel.ApplyRequested += OnAvailabilityApplyRequested;
		AvailabilityFilterViewModel.ClearRequested += OnAvailabilityClearRequested;
		AvailabilityFilterViewModel.CancelRequested += OnAvailabilityCancelRequested;
	}

	private void DetachHandlers()
	{
		if (_viewModel is null)
		{
			return;
		}

		_viewModel.CreateEventRequested -= OnCreateEventRequested;
		_viewModel.CustomAvailabilityRequested -= OnCustomAvailabilityRequested;
		_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		AvailabilityFilterViewModel.ApplyRequested -= OnAvailabilityApplyRequested;
		AvailabilityFilterViewModel.ClearRequested -= OnAvailabilityClearRequested;
		AvailabilityFilterViewModel.CancelRequested -= OnAvailabilityCancelRequested;
	}

	private async void OnViewModelPropertyChanged(
		object? sender,
		PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(MapViewModel.IsVenueSheetVisible)
			&& _viewModel?.IsVenueSheetVisible == false
			&& _completion is not null)
		{
			await CompleteAndCloseAsync();
		}
	}

	private async Task CompleteAndCloseAsync()
	{
		TaskCompletionSource? completion = _completion;
		if (completion is null)
		{
			return;
		}

		_completion = null;
		DetachHandlers();
		_viewModel?.DismissVenueSheetCommand.Execute(null);
		await Navigation.PopModalAsync();
		completion.TrySetResult();
	}

	private void CompleteWithoutNavigation()
	{
		TaskCompletionSource? completion = _completion;
		if (completion is null)
		{
			return;
		}

		_completion = null;
		_viewModel?.DismissVenueSheetCommand.Execute(null);
		completion.TrySetResult();
	}
	#endregion
}
