using ChoNaBojo.Contracts.Enums;

namespace ChoNaBojo.Server.Data.Entities;

/// <summary>
/// User account identity with separated credential and shareable contact fields.
/// </summary>
public class User
{
	/// <summary>Primary key; database-generated UUID.</summary>
	public Guid Id
	{
		get; set;
	}

	/// <summary>Credential email used for login.</summary>
	public string LoginEmail { get; set; } = null!;

	/// <summary>Normalized login email used for uniqueness and lookups.</summary>
	public string NormalizedLoginEmail { get; set; } = null!;

	/// <summary>Password hash produced by the configured hasher.</summary>
	public string PasswordHash { get; set; } = null!;

	/// <summary>Optional phone number shared after approval.</summary>
	public string? ContactPhone { get; set; }

	/// <summary>Optional shareable email address (separate from login email).</summary>
	public string? ContactEmail { get; set; }

	/// <summary>Optional communicator platform; requires <see cref="CommunicatorHandle"/>.</summary>
	public CommunicatorPlatform? CommunicatorPlatform { get; set; }

	/// <summary>Optional communicator handle; requires <see cref="CommunicatorPlatform"/>.</summary>
	public string? CommunicatorHandle { get; set; }

	/// <summary>UTC timestamp when the account was created.</summary>
	public DateTime CreatedUtc { get; set; }

	/// <summary>UTC timestamp of the last account update.</summary>
	public DateTime UpdatedUtc { get; set; }

	/// <summary>Refresh tokens issued to this user across login sessions.</summary>
	public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
