using System.Collections;
using Microsoft.Maui.Maps;
using ControlsMap = Microsoft.Maui.Controls.Maps.Map;
using MauiMap = Microsoft.Maui.Maps.IMap;

namespace ChoNaBojo.App.Views.Maps;

public sealed class VenueMap : ControlsMap
{
	#region Public methods
	public void ReplacePins(IEnumerable pins)
	{
		if (ReferenceEquals(ItemsSource, pins))
		{
			return;
		}

		if (Handler is not VenueMapHandler handler)
		{
			ItemsSource = pins;
			return;
		}

		// MAUI rebuilds every native marker after each templated pin is created.
		// Batch the source replacement so a filter change performs one rebuild instead.
		handler.SuppressPinUpdates = true;
		try
		{
			ItemsSource = pins;
		}
		finally
		{
			handler.SuppressPinUpdates = false;
		}

		Handler.UpdateValue(nameof(MauiMap.Pins));
	}
	#endregion
}
