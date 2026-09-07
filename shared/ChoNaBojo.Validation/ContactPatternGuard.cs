using System.Text.RegularExpressions;

namespace ChoNaBojo.Validation;

/// <summary>
/// Detects likely contact channels in user-authored text that is visible before event approval.
/// </summary>
public static class ContactPatternGuard
{
	#region Private static fields
	private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

	private static readonly Regex EmailPattern = new(
		"""(?<![A-Z0-9.!#$%&'*+/=?^_`{|}~-])[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?(?:\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)+""",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
		MatchTimeout);

	private static readonly Regex PhonePattern = new(
		"""(?<![\p{L}\p{N}])\+?(?:[ ().-]*\d){9,15}(?![ ().-]*\d)(?![\p{L}\p{N}])""",
		RegexOptions.Compiled | RegexOptions.CultureInvariant,
		MatchTimeout);

	private static readonly Regex UrlPattern = new(
		"""(?<![A-Z0-9_])(?:https?://|www\.)[^\s<>()]+""",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
		MatchTimeout);

	private static readonly Regex HandlePattern = new(
		"""(?<![A-Z0-9.!#$%&'*+/=?^_`{|}~@-])@[A-Z0-9_][A-Z0-9._]{0,63}(?![A-Z0-9._])""",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
		MatchTimeout);
	#endregion

	#region Public methods
	public static bool ContainsContactPattern(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		try
		{
			return EmailPattern.IsMatch(text)
				|| PhonePattern.IsMatch(text)
				|| UrlPattern.IsMatch(text)
				|| HandlePattern.IsMatch(text);
		}
		catch (RegexMatchTimeoutException)
		{
			// Treat an input that exhausts the bounded scan as unsafe rather than bypassing the guard.
			return true;
		}
	}
	#endregion
}
