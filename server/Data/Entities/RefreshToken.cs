namespace ChoNaBojo.Server.Data.Entities;

/// <summary>
/// Rotating refresh token row keyed to one user; stores only token hash at rest.
/// </summary>
public class RefreshToken
{
	public Guid Id
	{
		get; set;
	}

	public Guid UserId
	{
		get; set;
	}

	public User User { get; set; } = null!;

	/// <summary>SHA-256 hash of the opaque refresh token.</summary>
	public string TokenHash { get; set; } = null!;

	public Guid FamilyId { get; set; }

	public DateTime CreatedUtc { get; set; }

	public DateTime ExpiresUtc { get; set; }

	public DateTime? ConsumedUtc { get; set; }

	public DateTime? RevokedUtc { get; set; }

	public Guid? ReplacedByTokenId { get; set; }

	public string? CreatedByIp { get; set; }
}
