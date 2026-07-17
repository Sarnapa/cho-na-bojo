namespace ChoNaBojo.Server.Auth;

public static class AuthNormalization
{
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
}
