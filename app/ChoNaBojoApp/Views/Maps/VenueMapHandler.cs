using Microsoft.Maui.Maps;
using Microsoft.Maui.Maps.Handlers;
using MauiMap = Microsoft.Maui.Maps.IMap;

namespace ChoNaBojo.App.Views.Maps;

public sealed class VenueMapHandler : MapHandler
{
	#region Public static properties
	public static new IPropertyMapper<MauiMap, IMapHandler> Mapper =
		new PropertyMapper<MauiMap, IMapHandler>(MapHandler.Mapper)
		{
			[nameof(MauiMap.Pins)] = MapPins
		};
	#endregion

	#region Public properties
	public bool SuppressPinUpdates { get; set; }
	#endregion

	#region Constructors
	public VenueMapHandler()
		: base(Mapper, MapHandler.CommandMapper)
	{
	}
	#endregion

	#region Private static methods
	private static new void MapPins(IMapHandler handler, MauiMap map)
	{
		if (handler is VenueMapHandler { SuppressPinUpdates: true })
		{
			return;
		}

		MapHandler.MapPins(handler, map);
	}
	#endregion
}
