#if ANDROID
using Android.Gms.Maps.Model;
using ChoNaBojo.App.Views.Maps;
using Microsoft.Maui.Maps;
using Microsoft.Maui.Maps.Handlers;

namespace ChoNaBojo.App.Platforms.Android;

public sealed class VenuePinHandler: MapPinHandler
{
	#region IMapPinHandler implementation
	public static new IPropertyMapper<IMapPin, IMapPinHandler> Mapper =
		new PropertyMapper<IMapPin, IMapPinHandler>(MapPinHandler.Mapper)
		{
			[nameof(VenuePin.Hue)] = MapHue
		};
	#endregion

	#region Constructors
	public VenuePinHandler()
		: base(Mapper)
	{
	}
	#endregion

	#region Private methods
	private static void MapHue(IMapPinHandler handler, IMapPin mapPin)
	{
		if (mapPin is VenuePin venuePin)
		{
			handler.PlatformView.SetIcon(BitmapDescriptorFactory.DefaultMarker(venuePin.Hue));
		}
	}
	#endregion
}
#endif
