namespace ChoNaBojo.Utils.Text;

/// <summary>
/// Pure, stateless text helpers shared across the API and the mobile client.
/// No dependency on ASP.NET Core, EF Core, or MAUI.
/// </summary>
public static class TextNormalization
{
	#region Public methods
	public static string NormalizeLoginEmail(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		return value.Trim().ToUpperInvariant();
	}

	public static string? NormalizeOptionalText(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		return value.Trim();
	}

	public static bool IsBasicEmailShape(string value)
	{
		int atIndex = value.IndexOf('@');
		return atIndex > 0 && atIndex == value.LastIndexOf('@') && atIndex < value.Length - 1;
	}
	#endregion
}
