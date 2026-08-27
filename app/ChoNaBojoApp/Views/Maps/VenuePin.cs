using Microsoft.Maui.Controls.Maps;

namespace ChoNaBojo.App.Views.Maps;

public sealed class VenuePin : Pin
{
	#region Public static properties
	public static readonly BindableProperty HueProperty = BindableProperty.Create(
		nameof(Hue),
		typeof(float),
		typeof(VenuePin),
		120f);

	public static readonly BindableProperty VenueIdProperty = BindableProperty.Create(
		nameof(VenueId),
		typeof(int),
		typeof(VenuePin),
		0);
	#endregion

	#region Public properties
	public float Hue
	{
		get
		{
			return (float)GetValue(HueProperty);
		}

		set
		{
			SetValue(HueProperty, value);
		}
	}

	public int VenueId
	{
		get
		{
			return (int)GetValue(VenueIdProperty);
		}

		set
		{
			SetValue(VenueIdProperty, value);
		}
	}
	#endregion
}
