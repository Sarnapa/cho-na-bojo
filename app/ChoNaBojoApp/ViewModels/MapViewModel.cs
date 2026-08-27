using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Feedback;
using ChoNaBojo.App.Services.Venues;
using ChoNaBojo.App.Views.Maps;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;

namespace ChoNaBojo.App.ViewModels;

#region VenuePinViewData
public sealed record VenuePinViewData(
	int VenueId,
	float Hue,
	Location Location,
	string Label,
	string Address);
#endregion

public partial class MapViewModel : ViewModelBase
{
	#region Private static fields
	private static readonly Location WarsawCenter = new(52.2297, 21.0122);
	private static readonly Distance WarsawCenterInitialRadius = Distance.FromKilometers(5);
	private static readonly Distance InitialRadius = Distance.FromKilometers(2);
	#endregion

	#region Private fields
	private readonly IVenueCatalog _venueCatalog;
	private readonly ISessionService _sessionService;
	private readonly IFeedbackService _feedbackService;
	private IReadOnlyDictionary<int, string> _sportCodesById = new Dictionary<int, string>();
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
	private IReadOnlyList<VenuePinViewData> visiblePins = [];
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
	#endregion

	#region Commmands
	[RelayCommand]
	private Task AppearingAsync()
	{
		return LoadMapAsync();
	}

	[RelayCommand]
	private Task RetryAsync()
	{
		return LoadMapAsync();
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

	#region Private methods
	private async Task LoadMapAsync()
	{
		if (IsBusy)
		{
			return;
		}

		IsBusy = true;
		IsLoading = true;
		HasError = false;
		ErrorMessage = string.Empty;

		try
		{
			Task resolveLocationTask = ResolveInitialCenterAsync();
			Task<VenueCatalogLoadResult> catalogTask =
				_venueCatalog.EnsureLoadedAsync(CancellationToken.None);

			await Task.WhenAll(resolveLocationTask, catalogTask);
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
			BuildVisiblePins();
		}
		finally
		{
			IsLoading = false;
			IsBusy = false;
		}
	}

	private async Task ResolveInitialCenterAsync()
	{
		try
		{
			PermissionStatus status =
				await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
			if (status != PermissionStatus.Granted)
			{
				status = await MainThread.InvokeOnMainThreadAsync(
					Permissions.RequestAsync<Permissions.LocationWhenInUse>);
			}

			if (status != PermissionStatus.Granted)
			{
				UseWarsawFallback();
				return;
			}

			Location? location = await Geolocation.Default.GetLastKnownLocationAsync();
			location ??= await Geolocation.Default.GetLocationAsync(
				new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)),
				CancellationToken.None);

			if (location is null)
			{
				UseWarsawFallback();
				return;
			}

			LocationDenied = false;
			IsShowingUser = true;
			InitialCenter = MapSpan.FromCenterAndRadius(location, InitialRadius);
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
		IsShowingUser = false;
		InitialCenter = MapSpan.FromCenterAndRadius(WarsawCenter, WarsawCenterInitialRadius);
	}

	private void BuildVisiblePins()
	{
		VisiblePins = _venueCatalog.Venues
			.Select(venue => new VenuePinViewData(
				venue.Id,
				SportPinPalette.HueFor(venue.SportIds, SelectedSportId, _sportCodesById),
				new Location(venue.Latitude, venue.Longitude),
				venue.Name,
				venue.Address))
			.ToList();
	}
	#endregion
}
