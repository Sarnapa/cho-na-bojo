using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UraniumUI.Icons.MaterialSymbols;
using ChoNaBojo.App.Services;
using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Events;
using ChoNaBojo.App.Services.Feedback;
using ChoNaBojo.App.Services.Geocoding;
using ChoNaBojo.App.Services.Venues;
using ChoNaBojo.App.Views.Maps;
using ChoNaBojo.Contracts.Consts;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Contracts.Enums;
using ChoNaBojo.Validation;

namespace ChoNaBojo.App.ViewModels;

#region VenuePinViewData
public sealed record VenuePinViewData(
	int VenueId,
	float Hue,
	Location Location,
	string Label,
	string Address);
#endregion

#region VenueEventSportFilterViewData
public sealed record VenueEventSportFilterViewData(
	int? SportId,
	string Name,
	bool IsSelected)
{
	public string SemanticDescription => SportId.HasValue
		? $"Filter venue events by {Name}"
		: "Show events for all sports at this venue";
}
#endregion

#region SportFilterViewData
public sealed record SportFilterViewData(
	int? SportId,
	string Name,
	string IconGlyph,
	string SemanticDescription);
#endregion

#region EventAvailabilityFilterViewData
public sealed record EventAvailabilityFilterViewData(
	EventAvailabilityPreset Preset,
	string Name,
	bool IsSelected)
{
	public string SemanticDescription => $"Filter events by {Name}";
}
#endregion

#region VenueEventsFailureKind
public enum VenueEventsFailureKind
{
	None,
	Validation,
	ReferenceChanged,
	Unauthorized,
	Network,
	Unknown
}
#endregion

#region EventCardViewData
public sealed record EventCardViewData(
	Guid EventId,
	string Title,
	string? Description,
	DateTimeOffset StartsAtUtc,
	DateTimeOffset EstimatedEndsAtUtc,
	int ParticipantLimit,
	int ParticipantCount,
	bool AutoAccept,
	string SportName,
	bool IsOrganizer,
	EventJoinRequestStatus? CurrentUserRequestStatus,
	bool IsJoinInFlight)
{
	public bool HasDescription => !string.IsNullOrEmpty(Description);
	public bool IsFull => ParticipantCount >= ParticipantLimit;
	public bool CanJoin => !IsOrganizer
		&& CurrentUserRequestStatus is null
		&& !IsFull
		&& !IsJoinInFlight;
	public bool IsJoinButtonVisible => CanJoin || IsJoinInFlight;
	public bool IsStatusLabelVisible => !IsJoinButtonVisible;
	public string StartsAtDisplay => FormatLocalTime(StartsAtUtc);
	public string EstimatedEndsAtDisplay => FormatLocalTime(EstimatedEndsAtUtc);
	public string ParticipantDisplay => string.Create(
		CultureInfo.CurrentCulture,
		$"{ParticipantCount} / {ParticipantLimit}");
	public string ActionLabel => IsJoinInFlight
		? "Sending..."
		: IsOrganizer
			? "Your event"
			: CurrentUserRequestStatus switch
			{
				EventJoinRequestStatus.Pending => "Request pending",
				EventJoinRequestStatus.Accepted => "Joined",
				EventJoinRequestStatus.Rejected => "Request rejected",
				_ when IsFull => "Full",
				_ => "Join"
			};
	public string ActionSemanticDescription => IsJoinInFlight
		? $"Sending a join request for {Title}"
		: IsOrganizer
			? $"You organize {Title}"
			: CurrentUserRequestStatus switch
			{
				EventJoinRequestStatus.Pending => $"Join request pending for {Title}",
				EventJoinRequestStatus.Accepted => $"Joined {Title}",
				EventJoinRequestStatus.Rejected => $"Join request rejected for {Title}",
				_ when IsFull => $"{Title} is full",
				_ => $"Request to join {Title}"
			};

	public static EventCardViewData FromResponse(EventListItemResponse response)
	{
		return new EventCardViewData(
			response.EventId,
			response.Title,
			response.Description,
			response.StartsAtUtc,
			response.EstimatedEndsAtUtc,
			response.ParticipantLimit,
			response.ParticipantCount,
			response.AutoAccept,
			response.Sport.Name,
			response.IsOrganizer,
			response.CurrentUserRequestStatus,
			false);
	}

	private static string FormatLocalTime(DateTimeOffset utcValue)
	{
		DateTimeOffset localValue = TimeZoneInfo.ConvertTime(
			utcValue,
			TimeZoneInfo.Local);
		return localValue.ToString(
			"ddd, d MMM yyyy, HH:mm",
			CultureInfo.CurrentCulture);
	}
}
#endregion

#region MapCenterRequestedEventArgs
public sealed class MapCenterRequestedEventArgs : EventArgs
{
	#region Properties
	public MapSpan Region { get; }
	#endregion

	#region Constructors
	public MapCenterRequestedEventArgs(MapSpan region)
	{
		Region = region;
	}
	#endregion
}
#endregion

#region CreateEventRequestedEventArgs
public sealed class CreateEventRequestedEventArgs : EventArgs
{
	#region Properties
	public VenueResponse Venue { get; }
	public int? ActiveSportId { get; }
	#endregion

	#region Constructors
	public CreateEventRequestedEventArgs(VenueResponse venue, int? activeSportId)
	{
		Venue = venue;
		ActiveSportId = activeSportId;
	}
	#endregion
}
#endregion

public partial class MapViewModel : ViewModelBase
{
	#region Private static fields
	private static readonly Location WarsawCenter = new(52.2297, 21.0122);
	private static readonly Distance WarsawCenterInitialRadius = Distance.FromKilometers(5);
	private static readonly Distance InitialRadius = Distance.FromKilometers(2);
	private static readonly TimeSpan CachedLocationFreshness = TimeSpan.FromMinutes(2);
	#endregion

	#region Private fields
	private readonly IVenueCatalog _venueCatalog;
	private readonly ISessionService _sessionService;
	private readonly IFeedbackService _feedbackService;
	private readonly IApiService _apiService;
	private IReadOnlyDictionary<int, string> _sportCodesById = new Dictionary<int, string>();
	private IReadOnlyDictionary<int, string> _sportNamesById = new Dictionary<int, string>();
	private bool _hasLoadedMap;
	private Location? _lastKnownCurrentLocation;
	private EventAvailabilityWindow? _availabilityWindow;
	private CancellationTokenSource? _venueEventsCancellation;
	private CancellationTokenSource? _joinRequestCancellation;
	#endregion

	#region Observable properties
	[ObservableProperty]
	private bool isLoading;

	[ObservableProperty]
	private bool hasError;

	[ObservableProperty]
	private string errorMessage = string.Empty;

	[ObservableProperty]
	private MapSpan? initialCenter;

	[ObservableProperty]
	private bool locationDenied;

	[ObservableProperty]
	private bool isShowingUser;

	[ObservableProperty]
	private int? selectedSportId;

	[ObservableProperty]
	private IReadOnlyList<SportFilterViewData> sportFilters = [];

	[ObservableProperty]
	private SportFilterViewData? selectedSportFilter;

	[ObservableProperty]
	private IReadOnlyList<VenuePinViewData> visiblePins = [];

	[ObservableProperty]
	private bool hasNoVisibleVenues;

	[ObservableProperty]
	private VenueResponse? selectedVenue;

	[ObservableProperty]
	private int? selectedVenueEventSportId;

	[ObservableProperty]
	private IReadOnlyList<VenueEventSportFilterViewData> venueEventSportFilters = [];

	[ObservableProperty]
	private bool isVenueSheetVisible;

	[ObservableProperty]
	private ObservableCollection<EventCardViewData> venueEvents = [];

	[ObservableProperty]
	private bool isVenueEventsLoading;

	[ObservableProperty]
	private bool hasNoVenueEvents;

	[ObservableProperty]
	private bool hasVenueEventsError;

	[ObservableProperty]
	private string venueEventsErrorMessage = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanRetryVenueEvents))]
	private VenueEventsFailureKind venueEventsFailure;

	[ObservableProperty]
	private EventAvailabilityPreset selectedAvailabilityPreset =
		EventAvailabilityPreset.AnyTime;

	[ObservableProperty]
	private IReadOnlyList<EventAvailabilityFilterViewData> availabilityFilters =
		BuildAvailabilityFilters(EventAvailabilityPreset.AnyTime);

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(FilterActionLabel))]
	private bool areVenueEventFiltersVisible;

	[ObservableProperty]
	private bool isCustomAvailabilityEditorVisible;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(AddressSearchLabel))]
	private string addressQuery = string.Empty;

	[ObservableProperty]
	private bool isLocationBannerVisible;
	#endregion

	#region Constructors
	public MapViewModel(
		IVenueCatalog venueCatalog,
		ISessionService sessionService,
		IFeedbackService feedbackService,
		IApiService apiService)
	{
		_venueCatalog = venueCatalog;
		_sessionService = sessionService;
		_feedbackService = feedbackService;
		_apiService = apiService;
	}
	#endregion

	#region Events
	public event EventHandler? LoggedOut;
	public event EventHandler<MapCenterRequestedEventArgs>? MapCenterRequested;
	public event EventHandler<CreateEventRequestedEventArgs>? CreateEventRequested;
	public event EventHandler? CustomAvailabilityRequested;
	#endregion

	#region Public properties
	public string AddressSearchLabel
	{
		get
		{
			return string.IsNullOrWhiteSpace(AddressQuery)
				? "Search for an address"
				: AddressQuery;
		}
	}

	public bool CanRetryVenueEvents => VenueEventsFailure is
		VenueEventsFailureKind.Network or VenueEventsFailureKind.Unknown;

	public string FilterActionLabel => AreVenueEventFiltersVisible
		? "Hide filters"
		: "Filter";

	public bool HasCustomAvailability =>
		SelectedAvailabilityPreset == EventAvailabilityPreset.Custom
		&& _availabilityWindow is not null;

	public EventAvailabilityWindow? CustomAvailabilityWindow =>
		HasCustomAvailability ? _availabilityWindow : null;

	public string ActiveAvailabilitySummary
	{
		get
		{
			if (!HasCustomAvailability)
			{
				return string.Empty;
			}

			DateTimeOffset localStart = TimeZoneInfo.ConvertTime(
				_availabilityWindow!.AvailableFromUtc,
				TimeZoneInfo.Local);
			DateTimeOffset localEnd = TimeZoneInfo.ConvertTime(
				_availabilityWindow.AvailableToUtc,
				TimeZoneInfo.Local);
			return string.Create(
				CultureInfo.CurrentCulture,
				$"{localStart:d MMM, HH:mm} - {localEnd:d MMM, HH:mm}");
		}
	}
	#endregion

	#region Commmands
	[RelayCommand]
	private Task AppearingAsync(CancellationToken cancellationToken)
	{
		return LoadMapAsync(cancellationToken);
	}

	[RelayCommand]
	private Task RetryAsync(CancellationToken cancellationToken)
	{
		return LoadMapAsync(cancellationToken);
	}

	[RelayCommand]
	private void DismissVenueSheet()
	{
		CancelVenueScopedRequests();
		IsVenueSheetVisible = false;
		SelectedVenue = null;
		SelectedVenueEventSportId = null;
		VenueEventSportFilters = [];
		AreVenueEventFiltersVisible = false;
		IsCustomAvailabilityEditorVisible = false;
		ClearVenueEventState();
	}

	[RelayCommand]
	private void CreateEvent()
	{
		if (SelectedVenue is null)
		{
			return;
		}

		CreateEventRequested?.Invoke(
			this,
			new CreateEventRequestedEventArgs(
				SelectedVenue,
				SelectedVenueEventSportId));
	}

	[RelayCommand]
	private void ToggleVenueEventFilters()
	{
		AreVenueEventFiltersVisible = !AreVenueEventFiltersVisible;
		if (!AreVenueEventFiltersVisible)
		{
			IsCustomAvailabilityEditorVisible = false;
		}
	}

	[RelayCommand(AllowConcurrentExecutions = true)]
	private async Task SelectVenueEventSportFilterAsync(
		VenueEventSportFilterViewData? filter,
		CancellationToken cancellationToken)
	{
		if (filter is null
			|| VenueEventSportFilters.All(item => item.SportId != filter.SportId))
		{
			return;
		}

		SetSelectedVenueEventSport(filter.SportId);
		await ReloadSelectedVenueEventsAsync(cancellationToken);
	}

	[RelayCommand(AllowConcurrentExecutions = true)]
	private async Task SelectAvailabilityFilterAsync(
		EventAvailabilityFilterViewData? filter,
		CancellationToken cancellationToken)
	{
		if (filter is null)
		{
			return;
		}

		if (filter.Preset == EventAvailabilityPreset.Custom)
		{
			IsCustomAvailabilityEditorVisible = true;
			CustomAvailabilityRequested?.Invoke(this, EventArgs.Empty);
			return;
		}

		EventAvailabilityConversionResult conversion =
			EventAvailabilityConversion.ForPreset(filter.Preset);
		if (!conversion.IsValid)
		{
			SetVenueEventsFailure(
				VenueEventsFailureKind.Validation,
				FormatValidationErrors(conversion.Errors));
			VenueEvents.Clear();
			return;
		}

		_availabilityWindow = null;
		IsCustomAvailabilityEditorVisible = false;
		SetSelectedAvailability(filter.Preset);
		await ReloadSelectedVenueEventsAsync(cancellationToken);
	}

	[RelayCommand(CanExecute = nameof(CanRequestToJoinEvent))]
	private async Task RequestToJoinEventAsync(
		EventCardViewData? eventCard,
		CancellationToken cancellationToken)
	{
		if (eventCard is null
			|| SelectedVenue is null
			|| _joinRequestCancellation is not null)
		{
			return;
		}

		EventCardViewData? currentCard = VenueEvents.SingleOrDefault(
			item => item.EventId == eventCard.EventId);
		if (currentCard is null || !currentCard.CanJoin)
		{
			return;
		}

		var joinCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		CancellationToken joinToken = joinCancellation.Token;
		_joinRequestCancellation = joinCancellation;
		int selectedVenueId = SelectedVenue.Id;
		ReplaceEventCard(
			currentCard.EventId,
			card => card with { IsJoinInFlight = true });
		RequestToJoinEventCommand.NotifyCanExecuteChanged();

		try
		{
			JoinEventResult result = await _apiService.RequestToJoinEventAsync(
				currentCard.EventId,
				joinToken);
			joinToken.ThrowIfCancellationRequested();
			if (!IsCurrentVenue(selectedVenueId)
				|| !ReferenceEquals(_joinRequestCancellation, joinCancellation))
			{
				return;
			}

			switch (result.Status)
			{
				case JoinEventResultStatus.Success:
					bool listWasLoading = _venueEventsCancellation is not null;
					CancelVenueEventsLoad();
					ReplaceEventCard(
						currentCard.EventId,
						card => card with
						{
							CurrentUserRequestStatus = result.Response!.Status,
							IsJoinInFlight = false
						});
					if (listWasLoading)
					{
						await ReloadSelectedVenueEventsAsync(joinToken);
					}

					await _feedbackService.ShowSnackbarAsync(
						result.IsReplay
							? "Your existing join request was restored."
							: "Join request sent.",
						joinToken);
					break;

				case JoinEventResultStatus.Conflict:
					await _feedbackService.ShowSnackbarAsync(
						result.ConflictResponse!.Message,
						joinToken);
					await ReloadSelectedVenueEventsAsync(joinToken);
					break;

				case JoinEventResultStatus.Unauthorized:
					await _feedbackService.ShowSnackbarAsync(
						"Your session has expired. Please sign in again.",
						joinToken);
					break;

				case JoinEventResultStatus.Network:
					await _feedbackService.ShowSnackbarAsync(
						"The request result could not be confirmed. Check your connection and try Join again.",
						joinToken);
					break;

				default:
					await _feedbackService.ShowSnackbarAsync(
						"The server response could not be confirmed. Try Join again.",
						joinToken);
					break;
			}
		}
		catch (OperationCanceledException) when (joinToken.IsCancellationRequested)
		{
		}
		finally
		{
			if (ReferenceEquals(_joinRequestCancellation, joinCancellation))
			{
				_joinRequestCancellation = null;
				if (IsCurrentVenue(selectedVenueId))
				{
					ReplaceEventCard(
						currentCard.EventId,
						card => card with { IsJoinInFlight = false });
				}

				RequestToJoinEventCommand.NotifyCanExecuteChanged();
			}

			joinCancellation.Dispose();
		}
	}

	[RelayCommand]
	private Task RetryVenueEventsAsync(CancellationToken cancellationToken)
	{
		return ReloadSelectedVenueEventsAsync(cancellationToken);
	}

	[RelayCommand]
	private void DismissLocationBanner()
	{
		IsLocationBannerVisible = false;
	}

	[RelayCommand]
	private async Task RecenterOnCurrentLocationAsync()
	{
		try
		{
			PermissionStatus status =
				await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
			if (status != PermissionStatus.Granted)
			{
				UseWarsawFallback();
				await ShowCurrentLocationUnavailableAsync();
				return;
			}

			Location? location = GetFreshCachedLocation(
					await Geolocation.Default.GetLastKnownLocationAsync())
				?? GetFreshCachedLocation(_lastKnownCurrentLocation)
				?? await GetFreshCurrentLocationAsync();
			if (location is null)
			{
				UseWarsawFallback();
				await ShowCurrentLocationUnavailableAsync();
				return;
			}

			UseCurrentLocation(location);
		}
		catch (FeatureNotSupportedException)
		{
			UseWarsawFallback();
			await ShowCurrentLocationUnavailableAsync();
		}
		catch (FeatureNotEnabledException)
		{
			UseWarsawFallback();
			await ShowCurrentLocationUnavailableAsync();
		}
		catch (PermissionException)
		{
			UseWarsawFallback();
			await ShowCurrentLocationUnavailableAsync();
		}
		catch (TimeoutException)
		{
			UseWarsawFallback();
			await ShowCurrentLocationUnavailableAsync();
		}
	}

	[RelayCommand]
	private async Task LogoutAsync()
	{
		if (IsBusy)
		{
			return;
		}

		bool confirmed = await _feedbackService.ShowConfirmAsync(
			title: "Log out?",
			message: "You'll need to sign in again to continue.",
			confirmText: "Log out",
			cancelText: "Cancel");
		if (!confirmed)
		{
			return;
		}

		IsBusy = true;
		try
		{
			await _sessionService.SignOutAsync(revokeServer: true);
		}
		finally
		{
			IsBusy = false;
		}

		LoggedOut?.Invoke(this, EventArgs.Empty);
	}
	#endregion

	#region Public methods
	public void SelectVenue(int venueId)
	{
		VenueResponse venue = _venueCatalog.Venues.SingleOrDefault(item => item.Id == venueId)
			?? throw new InvalidOperationException(
				$"The selected venue with ID {venueId} is not present in the loaded catalog.");

		CancelVenueScopedRequests();
		ClearVenueEventState();
		SelectedVenue = venue;
		int? initialEventSportId = SelectedSportId.HasValue
			&& venue.SportIds.Contains(SelectedSportId.Value)
				? SelectedSportId
				: null;
		SetSelectedVenueEventSport(initialEventSportId);
		AreVenueEventFiltersVisible = false;
		IsCustomAvailabilityEditorVisible = false;
		IsVenueSheetVisible = true;
		_ = ReloadSelectedVenueEventsAsync();
	}

	public void CenterOnAddress(AddressSuggestion suggestion)
	{
		AddressQuery = suggestion.DisplayName;
		RequestMapCenter(MapSpan.FromCenterAndRadius(suggestion.Location, InitialRadius));
	}

	public void ApplyRefreshedCatalog(bool dismissSelectedVenue)
	{
		_sportCodesById = _venueCatalog.Sports.ToDictionary(
			sport => sport.Id,
			sport => sport.Code);
		_sportNamesById = _venueCatalog.Sports.ToDictionary(
			sport => sport.Id,
			sport => sport.Name);

		if (dismissSelectedVenue)
		{
			DismissVenueSheet();
		}
		else if (SelectedVenue is not null)
		{
			VenueResponse? refreshedVenue = _venueCatalog.Venues.SingleOrDefault(
				venue => venue.Id == SelectedVenue.Id);
			if (refreshedVenue is null)
			{
				DismissVenueSheet();
			}
			else
			{
				SelectedVenue = refreshedVenue;
				int? retainedEventSportId = SelectedVenueEventSportId.HasValue
					&& refreshedVenue.SportIds.Contains(SelectedVenueEventSportId.Value)
						? SelectedVenueEventSportId
						: null;
				SetSelectedVenueEventSport(retainedEventSportId);
			}
		}

		BuildSportFilters();
		BuildVisiblePins();
	}

	public Task ShowCreateFeedbackAsync(string message)
	{
		return _feedbackService.ShowSnackbarAsync(message);
	}

	public Task ReloadSelectedVenueEventsAsync(
		int expectedVenueId,
		CancellationToken cancellationToken = default)
	{
		return !IsCurrentVenue(expectedVenueId)
			? Task.CompletedTask
			: ReloadSelectedVenueEventsAsync(cancellationToken);
	}

	public async Task ApplyCustomAvailabilityAsync(
		EventAvailabilityWindow window,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(window);

		var query = new EventListingQuery(
			SelectedVenueEventSportId,
			window.AvailableFromUtc,
			window.AvailableToUtc);
		ValidationResult validation =
			EventListingValidation.ValidateEventListingQuery(query);
		if (!validation.IsValid)
		{
			SetVenueEventsFailure(
				VenueEventsFailureKind.Validation,
				FormatValidationErrors(validation.Errors));
			VenueEvents.Clear();
			return;
		}

		_availabilityWindow = window;
		IsCustomAvailabilityEditorVisible = false;
		SetSelectedAvailability(EventAvailabilityPreset.Custom);
		await ReloadSelectedVenueEventsAsync(cancellationToken);
	}

	public Task ClearAvailabilityAsync(CancellationToken cancellationToken = default)
	{
		_availabilityWindow = null;
		IsCustomAvailabilityEditorVisible = false;
		SetSelectedAvailability(EventAvailabilityPreset.AnyTime);
		return ReloadSelectedVenueEventsAsync(cancellationToken);
	}
	#endregion

	#region Observable property handlers
	partial void OnSelectedSportFilterChanged(SportFilterViewData? value)
	{
		SelectedSportId = value?.SportId;
	}

	partial void OnSelectedSportIdChanged(int? value)
	{
		BuildVisiblePins();
	}

	#endregion

	#region Private methods
	private Task ReloadSelectedVenueEventsAsync(
		CancellationToken cancellationToken = default)
	{
		if (SelectedVenue is null)
		{
			return Task.CompletedTask;
		}

		EventAvailabilityWindow? activeWindow = _availabilityWindow;
		if (SelectedAvailabilityPreset != EventAvailabilityPreset.Custom)
		{
			// Presets are deliberately recomputed per reload rather than pinned at selection time,
			// so "Today" keeps meaning today across midnight. Only Custom stays frozen in
			// _availabilityWindow.
			EventAvailabilityConversionResult conversion =
				EventAvailabilityConversion.ForPreset(SelectedAvailabilityPreset);
			if (!conversion.IsValid)
			{
				VenueEvents.Clear();
				SetVenueEventsFailure(
					VenueEventsFailureKind.Validation,
					FormatValidationErrors(conversion.Errors));
				return Task.CompletedTask;
			}

			activeWindow = conversion.Window;
		}

		CancelVenueEventsLoad();
		var venueEventsCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_venueEventsCancellation = venueEventsCancellation;

		int venueId = SelectedVenue.Id;
		var query = new EventListingQuery(
			SelectedVenueEventSportId,
			activeWindow?.AvailableFromUtc,
			activeWindow?.AvailableToUtc);
		return LoadVenueEventsAsync(
			venueId,
			query,
			venueEventsCancellation);
	}

	private async Task LoadVenueEventsAsync(
		int venueId,
		EventListingQuery query,
		CancellationTokenSource venueEventsCancellation)
	{
		CancellationToken venueEventsToken = venueEventsCancellation.Token;
		if (!ReferenceEquals(_venueEventsCancellation, venueEventsCancellation))
		{
			return;
		}
		IsVenueEventsLoading = true;
		IsVenueEventsLoading = true;
		HasNoVenueEvents = false;
		ClearVenueEventsFailure();

		try
		{
			ValidationResult validation =
				EventListingValidation.ValidateEventListingQuery(query);
			if (!validation.IsValid)
			{
				SetVenueEventsFailure(
					VenueEventsFailureKind.Validation,
					FormatValidationErrors(validation.Errors));
				return;
			}

			VenueEventListResult result = await _apiService.GetVenueEventsAsync(
				venueId,
				query,
				venueEventsToken);
			venueEventsToken.ThrowIfCancellationRequested();
			if (!ReferenceEquals(_venueEventsCancellation, venueEventsCancellation)
				|| !IsCurrentVenue(venueId))
			{
				return;
			}

			switch (result.Status)
			{
				case VenueEventListResultStatus.Success:
					UpdateVenueEvents(
						result.Events!.Select(EventCardViewData.FromResponse));
					HasNoVenueEvents = VenueEvents.Count == 0;
					break;

				case VenueEventListResultStatus.ValidationFailed:
					VenueEvents.Clear();
					SetVenueEventsFailure(
						VenueEventsFailureKind.Validation,
						FormatValidationErrors(result.ValidationErrors!));
					break;

				case VenueEventListResultStatus.ReferenceChanged:
					VenueEvents.Clear();
					await RecoverFromVenueEventsReferenceChangeAsync(
						result.Conflict!,
						venueEventsToken);
					break;

				case VenueEventListResultStatus.Unauthorized:
					VenueEvents.Clear();
					SetVenueEventsFailure(
						VenueEventsFailureKind.Unauthorized,
						"Your session has expired. Please sign in again.");
					break;

				case VenueEventListResultStatus.Network:
					VenueEvents.Clear();
					SetVenueEventsFailure(
						VenueEventsFailureKind.Network,
						"Can't load events. Check your connection and try again.");
					break;

				default:
					VenueEvents.Clear();
					SetVenueEventsFailure(
						VenueEventsFailureKind.Unknown,
						"The events couldn't be loaded. Please try again.");
					break;
			}
		}
		catch (OperationCanceledException)
			when (venueEventsToken.IsCancellationRequested)
		{
		}
		finally
		{
			if (ReferenceEquals(_venueEventsCancellation, venueEventsCancellation))
			{
				_venueEventsCancellation = null;
				IsVenueEventsLoading = false;
			}

			venueEventsCancellation.Dispose();
		}
	}

	private async Task LoadMapAsync(CancellationToken cancellationToken)
	{
		if (IsBusy || _hasLoadedMap)
		{
			return;
		}

		IsBusy = true;
		IsLoading = true;
		HasError = false;
		ErrorMessage = string.Empty;

		try
		{
			Task resolveLocationTask = ResolveInitialCenterAsync(cancellationToken);
			Task<VenueCatalogLoadResult> catalogTask =
				_venueCatalog.EnsureLoadedAsync(cancellationToken);

			await Task.WhenAll(resolveLocationTask, catalogTask);
			cancellationToken.ThrowIfCancellationRequested();
			VenueCatalogLoadResult catalogResult = await catalogTask;

			if (catalogResult.Status != VenueCatalogLoadStatus.Success)
			{
				VisiblePins = [];
				HasError = true;
				ErrorMessage = catalogResult.Status switch
				{
					VenueCatalogLoadStatus.Network =>
						"Can't reach the server. Check your connection and try again.",
					VenueCatalogLoadStatus.Unauthorized =>
						"Your session has expired. Please sign in again.",
					_ => "The venues couldn't be loaded. Please try again."
				};
				return;
			}

			_sportCodesById = _venueCatalog.Sports.ToDictionary(
				sport => sport.Id,
				sport => sport.Code);
			_sportNamesById = _venueCatalog.Sports.ToDictionary(
				sport => sport.Id,
				sport => sport.Name);
			BuildSportFilters();
			BuildVisiblePins();
			_hasLoadedMap = true;
		}
		finally
		{
			IsLoading = false;
			IsBusy = false;
		}
	}

	private async Task ResolveInitialCenterAsync(CancellationToken cancellationToken)
	{
		try
		{
			PermissionStatus status =
				await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
			cancellationToken.ThrowIfCancellationRequested();
			if (status != PermissionStatus.Granted)
			{
				status = await MainThread.InvokeOnMainThreadAsync(
					Permissions.RequestAsync<Permissions.LocationWhenInUse>);
				cancellationToken.ThrowIfCancellationRequested();
			}

			if (status != PermissionStatus.Granted)
			{
				UseWarsawFallback();
				return;
			}

			Location? location = await GetInitialLocationAsync(cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();

			if (location is null)
			{
				UseWarsawFallback();
				return;
			}

			UseCurrentLocation(location);
		}
		catch (FeatureNotSupportedException)
		{
			UseWarsawFallback();
		}
		catch (FeatureNotEnabledException)
		{
			UseWarsawFallback();
		}
		catch (PermissionException)
		{
			UseWarsawFallback();
		}
		catch (TimeoutException)
		{
			UseWarsawFallback();
		}
	}

	private void UseWarsawFallback()
	{
		LocationDenied = true;
		IsLocationBannerVisible = true;
		IsShowingUser = false;
		RequestMapCenter(
			MapSpan.FromCenterAndRadius(WarsawCenter, WarsawCenterInitialRadius));
	}

	private static async Task<Location?> GetInitialLocationAsync(
		CancellationToken cancellationToken)
	{
		Location? location = GetFreshCachedLocation(
			await Geolocation.Default.GetLastKnownLocationAsync());
		cancellationToken.ThrowIfCancellationRequested();
		return location ?? await GetFreshCurrentLocationAsync(cancellationToken);
	}

	private static Location? GetFreshCachedLocation(Location? location)
	{
		if (location is null)
		{
			return null;
		}

		TimeSpan age = DateTimeOffset.UtcNow - location.Timestamp;
		return age <= CachedLocationFreshness ? location : null;
	}

	private static Task<Location?> GetFreshCurrentLocationAsync()
	{
		return GetFreshCurrentLocationAsync(CancellationToken.None);
	}

	private static Task<Location?> GetFreshCurrentLocationAsync(
		CancellationToken cancellationToken)
	{
		return Geolocation.Default.GetLocationAsync(
			new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)),
			cancellationToken);
	}

	private void UseCurrentLocation(Location location)
	{
		_lastKnownCurrentLocation = location;
		LocationDenied = false;
		IsLocationBannerVisible = false;
		IsShowingUser = true;
		RequestMapCenter(MapSpan.FromCenterAndRadius(location, InitialRadius));
	}

	private void RequestMapCenter(MapSpan region)
	{
		InitialCenter = region;
		MapCenterRequested?.Invoke(this, new MapCenterRequestedEventArgs(region));
	}

	private void BuildSportFilters()
	{
		var allFilter = new SportFilterViewData(
			null,
			"All",
			MaterialOutlined.Sports,
			"Show all venues");
		IReadOnlyList<SportFilterViewData> filters =
		[
			allFilter,
			.. _venueCatalog.Sports.Select(sport => new SportFilterViewData(
				sport.Id,
				sport.Name,
				IconFor(sport.Code),
				$"Filter venues by {sport.Name}"))
		];

		int? currentSportId = SelectedSportId;
		SportFilters = filters;
		SelectedSportFilter = filters.FirstOrDefault(filter => filter.SportId == currentSportId)
			?? allFilter;
	}

	private void BuildVisiblePins()
	{
		IReadOnlyList<VenuePinViewData> pins = _venueCatalog.Venues
			.Where(venue =>
				SelectedSportId is null || venue.SportIds.Contains(SelectedSportId.Value))
			.Select(venue => new VenuePinViewData(
				venue.Id,
				SportPinPalette.HueFor(venue.SportIds, SelectedSportId, _sportCodesById),
				new Location(venue.Latitude, venue.Longitude),
				venue.Name,
				venue.Address))
			.ToList();

		VisiblePins = pins;
		HasNoVisibleVenues = SelectedSportId is not null && pins.Count == 0;
		if (SelectedVenue is not null
			&& pins.All(pin => pin.VenueId != SelectedVenue.Id))
		{
			DismissVenueSheet();
		}
	}

	private static IReadOnlyList<EventAvailabilityFilterViewData>
		BuildAvailabilityFilters(EventAvailabilityPreset selectedPreset)
	{
		return
		[
			new(
				EventAvailabilityPreset.AnyTime,
				"Any time",
				selectedPreset == EventAvailabilityPreset.AnyTime),
			new(
				EventAvailabilityPreset.Today,
				"Today",
				selectedPreset == EventAvailabilityPreset.Today),
			new(
				EventAvailabilityPreset.Tomorrow,
				"Tomorrow",
				selectedPreset == EventAvailabilityPreset.Tomorrow),
			new(
				EventAvailabilityPreset.NextSevenDays,
				"Next 7 days",
				selectedPreset == EventAvailabilityPreset.NextSevenDays),
			new(
				EventAvailabilityPreset.Custom,
				"Custom",
				selectedPreset == EventAvailabilityPreset.Custom)
		];
	}

	private void SetSelectedVenueEventSport(int? sportId)
	{
		SelectedVenueEventSportId = sportId;
		VenueEventSportFilters =
		[
			new VenueEventSportFilterViewData(
				null,
				"All sports",
				sportId is null),
			.. (SelectedVenue?.SportIds ?? [])
				.Select(id => new VenueEventSportFilterViewData(
					id,
					ResolveSportName(id),
					id == sportId))
		];
	}

	private async Task RecoverFromVenueEventsReferenceChangeAsync(
		EventConflictResponse conflict,
		CancellationToken cancellationToken)
	{
		VenueCatalogLoadResult refreshResult = await _venueCatalog.RefreshAsync(
			cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		if (refreshResult.Status == VenueCatalogLoadStatus.Success)
		{
			bool dismissSelectedVenue = string.Equals(
				conflict.Code,
				EventConflictCodes.VenueNotFound,
				StringComparison.Ordinal);
			ApplyRefreshedCatalog(dismissSelectedVenue);
			await _feedbackService.ShowSnackbarAsync(
				$"{conflict.Message} The map has been refreshed.",
				CancellationToken.None);
			return;
		}

		VenueEventsFailureKind failureKind = refreshResult.Status switch
		{
			VenueCatalogLoadStatus.Unauthorized => VenueEventsFailureKind.Unauthorized,
			VenueCatalogLoadStatus.Network => VenueEventsFailureKind.Network,
			_ => VenueEventsFailureKind.ReferenceChanged
		};
		string message = refreshResult.Status switch
		{
			VenueCatalogLoadStatus.Unauthorized =>
				"Your session expired while refreshing the venue.",
			VenueCatalogLoadStatus.Network =>
				$"{conflict.Message} The venue catalog could not be refreshed. Check your connection and try again.",
			_ => $"{conflict.Message} The venue catalog could not be refreshed."
		};
		SetVenueEventsFailure(failureKind, message);
	}

	private void SetSelectedAvailability(EventAvailabilityPreset preset)
	{
		SelectedAvailabilityPreset = preset;
		AvailabilityFilters = BuildAvailabilityFilters(preset);
		OnPropertyChanged(nameof(HasCustomAvailability));
		OnPropertyChanged(nameof(CustomAvailabilityWindow));
		OnPropertyChanged(nameof(ActiveAvailabilitySummary));
	}

	private void SetVenueEventsFailure(
		VenueEventsFailureKind kind,
		string message)
	{
		VenueEventsFailure = kind;
		VenueEventsErrorMessage = message;
		HasVenueEventsError = true;
		HasNoVenueEvents = false;
	}

	private void ClearVenueEventsFailure()
	{
		VenueEventsFailure = VenueEventsFailureKind.None;
		VenueEventsErrorMessage = string.Empty;
		HasVenueEventsError = false;
	}

	private void ClearVenueEventState()
	{
		VenueEvents.Clear();
		IsVenueEventsLoading = false;
		HasNoVenueEvents = false;
		ClearVenueEventsFailure();
	}

	private static string FormatValidationErrors(
		IReadOnlyDictionary<string, string[]> errors)
	{
		string message = string.Join(
			" ",
			errors.SelectMany(error => error.Value));
		return string.IsNullOrWhiteSpace(message)
			? "The availability filter is invalid."
			: message;
	}

	private void ReplaceEventCard(
		Guid eventId,
		Func<EventCardViewData, EventCardViewData> replace)
	{
		int index = VenueEvents
			.Select((card, cardIndex) => (card, cardIndex))
			.Where(item => item.card.EventId == eventId)
			.Select(item => item.cardIndex)
			.DefaultIfEmpty(-1)
			.Single();
		if (index < 0)
		{
			return;
		}

		VenueEvents[index] = replace(VenueEvents[index]);
	}

	private void UpdateVenueEvents(IEnumerable<EventCardViewData> events)
	{
		IReadOnlyList<EventCardViewData> updated = events.ToList();
		int sharedCount = Math.Min(VenueEvents.Count, updated.Count);

		for (int index = 0; index < sharedCount; index++)
		{
			VenueEvents[index] = updated[index];
		}

		while (VenueEvents.Count > updated.Count)
		{
			VenueEvents.RemoveAt(VenueEvents.Count - 1);
		}

		for (int index = sharedCount; index < updated.Count; index++)
		{
			VenueEvents.Add(updated[index]);
		}
	}

	private bool CanRequestToJoinEvent(EventCardViewData? eventCard)
	{
		return eventCard is not null
			&& eventCard.CanJoin
			&& _joinRequestCancellation is null
			&& IsVenueSheetVisible;
	}

	private bool IsCurrentVenue(int venueId)
	{
		return IsVenueSheetVisible && SelectedVenue?.Id == venueId;
	}

	private void CancelVenueScopedRequests()
	{
		CancelVenueEventsLoad();

		CancellationTokenSource? joinCancellation =
			Interlocked.Exchange(ref _joinRequestCancellation, null);
		joinCancellation?.Cancel();
		joinCancellation?.Dispose();
		RequestToJoinEventCommand.NotifyCanExecuteChanged();
	}

	private void CancelVenueEventsLoad()
	{
		CancellationTokenSource? venueEventsCancellation =
			Interlocked.Exchange(ref _venueEventsCancellation, null);
		venueEventsCancellation?.Cancel();
		venueEventsCancellation?.Dispose();
		IsVenueEventsLoading = false;
	}

	private static string IconFor(string sportCode)
	{
		return sportCode switch
		{
			"football" => MaterialOutlined.Sports_soccer,
			"basketball" => MaterialOutlined.Sports_basketball,
			"volleyball" => MaterialOutlined.Sports_volleyball,
			"tennis" => MaterialOutlined.Sports_tennis,
			"running" => MaterialOutlined.Directions_run,
			"cycling" => MaterialOutlined.Cycle,
			"rollerblading" => MaterialOutlined.Roller_skating,
			"gym" => MaterialOutlined.Fitness_center,
			"street_workout" => MaterialOutlined.Sports_gymnastics,
			"swimming" => MaterialOutlined.Pool,
			_ => MaterialOutlined.Sports
		};
	}

	private string ResolveSportName(int sportId)
	{
		if (_sportNamesById.TryGetValue(sportId, out string? sportName))
		{
			return sportName;
		}

		throw new InvalidOperationException(
			$"Sport ID {sportId} referenced by a venue is not present in the loaded catalog.");
	}

	private Task ShowCurrentLocationUnavailableAsync()
	{
		return _feedbackService.ShowSnackbarAsync(
			"Current location isn't available");
	}
	#endregion
}
