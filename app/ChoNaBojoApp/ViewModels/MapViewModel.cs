using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UraniumUI.Icons.MaterialSymbols;
using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Feedback;
using ChoNaBojo.App.Services.Geocoding;
using ChoNaBojo.App.Services.Venues;
using ChoNaBojo.App.Views.Maps;
using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.App.ViewModels;

#region VenuePinViewData
public sealed record VenuePinViewData(
	int VenueId,
	float Hue,
	Location Location,
	string Label,
	string Address);
#endregion

#region VenueSportViewData
public sealed record VenueSportViewData(string Name);
#endregion

#region SportFilterViewData
public sealed record SportFilterViewData(
	int? SportId,
	string Name,
	string IconGlyph,
	string SemanticDescription);
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
	private IReadOnlyDictionary<int, string> _sportCodesById = new Dictionary<int, string>();
	private IReadOnlyDictionary<int, string> _sportNamesById = new Dictionary<int, string>();
	private bool _hasLoadedMap;
	private Location? _lastKnownCurrentLocation;
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
	private IReadOnlyList<VenueSportViewData> selectedVenueSports = [];

	[ObservableProperty]
	private bool isVenueSheetVisible;

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
		IFeedbackService feedbackService)
	{
		_venueCatalog = venueCatalog;
		_sessionService = sessionService;
		_feedbackService = feedbackService;
	}
	#endregion

	#region Events
	public event EventHandler? LoggedOut;
	public event EventHandler<MapCenterRequestedEventArgs>? MapCenterRequested;
	public event EventHandler<CreateEventRequestedEventArgs>? CreateEventRequested;
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
		IsVenueSheetVisible = false;
		SelectedVenue = null;
		SelectedVenueSports = [];
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
			new CreateEventRequestedEventArgs(SelectedVenue, SelectedSportId));
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

		IReadOnlyList<VenueSportViewData> sports = venue.SportIds
			.Select(sportId => new VenueSportViewData(ResolveSportName(sportId)))
			.ToList();

		SelectedVenue = venue;
		SelectedVenueSports = sports;
		IsVenueSheetVisible = true;
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
				SelectedVenueSports = refreshedVenue.SportIds
					.Select(sportId => new VenueSportViewData(ResolveSportName(sportId)))
					.ToList();
			}
		}

		BuildSportFilters();
		BuildVisiblePins();
	}

	public Task ShowCreateFeedbackAsync(string message)
	{
		return _feedbackService.ShowSnackbarAsync(message);
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
