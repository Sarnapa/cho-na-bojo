namespace ChoNaBojo.App.Views.Maps;

public static class SportPinPalette
{
	#region Private constants
	private const float MultiSportHue = 15f; // red orange
	#endregion

	#region Private static fields
	private static readonly IReadOnlyDictionary<string, float> HuesBySportCode =
		new Dictionary<string, float>(StringComparer.Ordinal)
		{
			["football"] = 120f, // green
			["basketball"] = 35f, // orange
			["volleyball"] = 60f, // yellow
			["tennis"] = 75f, // yellow green
			["running"] = 150f, // mint
			["cycling"] = 300f, // magenta
			["rollerblading"] = 330f, // pink
			["gym"] = 270f, // violet
			["street_workout"] = 240f, // navy blue
			["swimming"] = 210f // blue
		};
	#endregion

	#region Public static methods
	public static float HueFor(
		IReadOnlyList<int> sportIds,
		int? selectedSportId,
		IReadOnlyDictionary<int, string> sportCodesById)
	{
		int? sportId = selectedSportId ?? (sportIds.Count == 1 ? sportIds[0] : null);
		if (sportId is null
			|| !sportCodesById.TryGetValue(sportId.Value, out string? sportCode)
			|| !HuesBySportCode.TryGetValue(sportCode, out float hue))
		{
			return MultiSportHue;
		}

		return hue;
	}
	#endregion

	// .NET 11's Pin.ImageSource should replace hue-only markers with sport glyphs that
	// follow ui-guidelines section 7.
}
