#if ANDROID
using Android.Gms.Maps;
using Android.OS;
using MauiLocation = Microsoft.Maui.Devices.Sensors.Location;
using NativeLocation = Android.Locations.Location;

namespace ChoNaBojo.App.Platforms.Android;

public sealed class CurrentLocationSource : Java.Lang.Object, ILocationSource
{
	#region Private fields
	private ILocationSourceOnLocationChangedListener? _listener;
	private NativeLocation? _location;
	#endregion

	#region Public methods
	public void Activate(ILocationSourceOnLocationChangedListener listener)
	{
		_listener = listener;
		PublishLocation();
	}

	public void Deactivate()
	{
		_listener = null;
	}

	public void Update(MauiLocation location)
	{
		_location?.Dispose();
		_location = new NativeLocation("ChoNaBojo")
		{
			Latitude = location.Latitude,
			Longitude = location.Longitude,
			Accuracy = 10f,
			Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			ElapsedRealtimeNanos = SystemClock.ElapsedRealtimeNanos()
		};

		PublishLocation();
	}
	#endregion

	#region Overrides
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_listener = null;
			_location?.Dispose();
			_location = null;
		}

		base.Dispose(disposing);
	}
	#endregion

	#region Private methods
	private void PublishLocation()
	{
		if (_location is not null)
		{
			_listener?.OnLocationChanged(_location);
		}
	}
	#endregion
}
#endif
