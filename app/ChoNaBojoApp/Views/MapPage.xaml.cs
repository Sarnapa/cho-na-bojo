using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using ChoNaBojo.App.Services.Geocoding;
using ChoNaBojo.App.Services.Navigation;
using ChoNaBojo.App.ViewModels;
using ChoNaBojo.App.Views.Maps;
#if ANDROID
using Android.Gms.Maps;
using Android.Gms.Maps.Model;
using ChoNaBojo.App.Platforms.Android;
using Microsoft.Maui.Maps.Handlers;
#endif

namespace ChoNaBojo.App.Views;

public partial class MapPage : ContentPage
{
	#region Private fields
	private readonly INavigationRootService _navigationRootService;
	private readonly IServiceProvider _serviceProvider;
	private readonly MapViewModel _viewModel;
	private bool _isAppeared;
	private bool _hasAppliedMapRegion;
	private bool _isOpeningVenueEvents;
#if ANDROID
	private readonly CurrentLocationSource _currentLocationSource = new();
#endif
	#endregion

	#region Constructors
	public MapPage(
		MapViewModel viewModel,
		INavigationRootService navigationRootService,
		IServiceProvider serviceProvider)
	{
		InitializeComponent();
		_viewModel = viewModel;
		_navigationRootService = navigationRootService;
		_serviceProvider = serviceProvider;
		BindingContext = _viewModel;
	}
	#endregion

	#region Overrides
	protected override void OnAppearing()
	{
		base.OnAppearing();
		_isAppeared = true;
		_viewModel.LoggedOut += OnLoggedOut;
		_viewModel.MapCenterRequested += OnMapCenterRequested;
		_viewModel.PropertyChanged += OnViewModelPropertyChanged;
		ReplaceVisiblePins();

		if (!_hasAppliedMapRegion && _viewModel.InitialCenter is not null)
		{
			ShowInitialRegion(_viewModel.InitialCenter);
		}

		_viewModel.AppearingCommand.Execute(null);
	}

	protected override void OnDisappearing()
	{
		_isAppeared = false;
		_viewModel.AppearingCommand.Cancel();
		_viewModel.RetryCommand.Cancel();
		DisableUserLocationLayer();
		_viewModel.LoggedOut -= OnLoggedOut;
		_viewModel.MapCenterRequested -= OnMapCenterRequested;
		_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		base.OnDisappearing();
	}
	#endregion

	#region Events handlers
	private async void OnPinClicked(object? sender, PinClickedEventArgs e)
	{
		e.HideInfoWindow = true;

		if (sender is not VenuePin venuePin)
		{
			throw new InvalidOperationException(
				$"Expected the event sender to be a {nameof(VenuePin)}.");
		}

		if (_isOpeningVenueEvents)
		{
			return;
		}

		_isOpeningVenueEvents = true;
		try
		{
			VenueEventsPage venueEventsPage =
				_serviceProvider.GetRequiredService<VenueEventsPage>();
			await venueEventsPage.ShowAsync(
				Navigation,
				_viewModel,
				venuePin.VenueId);
		}
		finally
		{
			_isOpeningVenueEvents = false;
		}
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(MapViewModel.VisiblePins))
		{
			if (Dispatcher.IsDispatchRequired)
			{
				Dispatcher.Dispatch(ReplaceVisiblePins);
			}
			else
			{
				ReplaceVisiblePins();
			}
		}

		if (e.PropertyName == nameof(MapViewModel.IsShowingUser))
		{
			ConfigureUserLocationLayer();
		}
	}

	private void OnMapCenterRequested(object? sender, MapCenterRequestedEventArgs e)
	{
		if (Dispatcher.IsDispatchRequired)
		{
			Dispatcher.Dispatch(() => ShowInitialRegion(e.Region));
		}
		else
		{
			ShowInitialRegion(e.Region);
		}
	}

	private async void OnLoggedOut(object? sender, EventArgs e)
	{
		Task? executionTask = _viewModel.LogoutCommand.ExecutionTask;
		if (executionTask is not null)
		{
			await executionTask.ConfigureAwait(true);
		}

		_navigationRootService.SetAuthRoot();
	}

	private async void OnAddressSearchTapped(object? sender, TappedEventArgs e)
	{
		AddressSearchPage searchPage =
			_serviceProvider.GetRequiredService<AddressSearchPage>();
		AddressSuggestion? suggestion =
			await searchPage.ShowAsync(Navigation, _viewModel.AddressQuery);
		if (suggestion is not null)
		{
			_viewModel.CenterOnAddress(suggestion);
		}
	}

	#endregion

	#region Private methods
	private void ShowInitialRegion(MapSpan region)
	{
		_hasAppliedMapRegion = true;
#if ANDROID
		if (VenueMap.Handler is MapHandler { Map: not null } handler)
		{
			var center = new LatLng(region.Center.Latitude, region.Center.Longitude);
			handler.Map.MoveCamera(CameraUpdateFactory.NewLatLngZoom(center, 13f));
		}
		else
#endif
		{
			// When the Android map is not ready yet, MAUI queues this region and applies it
			// without animation during the native map's initial layout.
			VenueMap.MoveToRegion(region);
		}

		ConfigureUserLocationLayer();
	}

	private void ReplaceVisiblePins()
	{
		VenueMap.ReplacePins(_viewModel.VisiblePins);
	}

	private async void ConfigureUserLocationLayer()
	{
#if ANDROID
		if (!_isAppeared || VenueMap.Handler is not MapHandler handler)
		{
			return;
		}

		GoogleMap map;
		if (handler.Map is not null)
		{
			map = handler.Map;
		}
		else
		{
			if (!_viewModel.IsShowingUser || _viewModel.InitialCenter is null)
			{
				return;
			}

			using var callback = new GoogleMapReadyCallback();
			handler.PlatformView.GetMapAsync(callback);
			map = await callback.MapReady;
		}

		if (!_isAppeared
			|| !_viewModel.IsShowingUser
			|| _viewModel.InitialCenter is null)
		{
			ApplyUserLocationLayer(map, false);
			return;
		}

		PermissionStatus permission =
			await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
		if (permission != PermissionStatus.Granted)
		{
			ApplyUserLocationLayer(map, false);
			return;
		}

		Location currentLocation = _viewModel.InitialCenter.Center;
		await MainThread.InvokeOnMainThreadAsync(() =>
		{
			bool shouldEnable = _isAppeared && _viewModel.IsShowingUser;
			if (shouldEnable)
			{
				_currentLocationSource.Update(currentLocation);
			}

			ApplyUserLocationLayer(map, shouldEnable);
		});
#else
		await Task.CompletedTask;
#endif
	}

	private void DisableUserLocationLayer()
	{
#if ANDROID
		if (VenueMap.Handler is MapHandler { Map: not null } handler)
		{
			ApplyUserLocationLayer(handler.Map, false);
		}
#endif
	}

#if ANDROID
	private void ApplyUserLocationLayer(GoogleMap map, bool isEnabled)
	{
		map.MyLocationEnabled = false;
		map.UiSettings.MyLocationButtonEnabled = false;
		if (!isEnabled)
		{
			return;
		}

		map.SetLocationSource(_currentLocationSource);
		map.MyLocationEnabled = true;
	}
#endif
	#endregion

	#region GoogleMapReadyCallback
#if ANDROID
	private sealed class GoogleMapReadyCallback : Java.Lang.Object, IOnMapReadyCallback
	{
		private readonly TaskCompletionSource<GoogleMap> _mapReady =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<GoogleMap> MapReady
		{
			get
			{
				return _mapReady.Task;
			}
		}

		public void OnMapReady(GoogleMap googleMap)
		{
			_mapReady.TrySetResult(googleMap);
		}
	}
#endif
	#endregion
}
