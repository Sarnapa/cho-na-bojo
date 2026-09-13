using System.Diagnostics;
using ChoNaBojo.Validation;

namespace ChoNaBojo.UnitTests.Validation;

public sealed class ContactPatternGuardTests
{
	#region Test methods
	[Theory]
	[InlineData("Write to player@example.com")]
	[InlineData("Call +48 (123) 456-789")]
	[InlineData("Call 123 456 789")]
	[InlineData("Call 123-456-789")]
	[InlineData("Details at https://example.com/game")]
	[InlineData("Details at www.example.com/game")]
	[InlineData("Message @player_name")]
	public void Contact_channels_are_detected(string text)
	{
		Assert.True(ContactPatternGuard.ContainsContactPattern(text));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" \t\r\n ")]
	[InlineData("Football at 18:30 on Court 2")]
	[InlineData("Bring water and blue shirts")]
	public void Empty_or_legitimate_event_text_is_allowed(string? text)
	{
		Assert.False(ContactPatternGuard.ContainsContactPattern(text));
	}

	[Theory]
	[InlineData("12345678", false)]
	[InlineData("123456789", true)]
	[InlineData("123456789012345", true)]
	[InlineData("1234567890123456", false)]
	public void Phone_digit_boundaries_are_enforced(string text, bool expected)
	{
		Assert.Equal(expected, ContactPatternGuard.ContainsContactPattern(text));
	}

	[Fact]
	public void Adversarial_input_is_classified_within_a_bounded_time()
	{
		string text = string.Concat(Enumerable.Repeat("a@", 1_000_000));
		var stopwatch = Stopwatch.StartNew();

		bool containsContactPattern = ContactPatternGuard.ContainsContactPattern(text);

		stopwatch.Stop();
		Assert.True(containsContactPattern);
		Assert.True(
			stopwatch.Elapsed < TimeSpan.FromSeconds(2),
			$"Contact scan took {stopwatch.Elapsed.TotalMilliseconds:0} ms.");
	}
	#endregion
}
