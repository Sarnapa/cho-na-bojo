namespace ChoNaBojo.Contracts.Consts;

/// <summary>
/// Event creation bounds shared by API, client, and persistence configuration.
/// </summary>
public static class EventPolicy
{
	public const int TitleMaxLength = 100;
	public const int DescriptionMaxLength = 1000;
	public const int ParticipantLimitMinimum = 2;
	public const int ParticipantLimitMaximum = 300;

	public static readonly TimeSpan MaximumDuration = TimeSpan.FromHours(24);
	public static readonly TimeSpan StartClockSkewTolerance = TimeSpan.FromMinutes(2);
}
