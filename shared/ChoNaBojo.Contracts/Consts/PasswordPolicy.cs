namespace ChoNaBojo.Contracts.Consts;

/// <summary>
/// Password policy bounds shared by authoritative server validation and
/// client-side UX validation so the two can never drift.
/// </summary>
public static class PasswordPolicy
{
	public const int MinLength = 8;
	public const int MaxLength = 128;
}
