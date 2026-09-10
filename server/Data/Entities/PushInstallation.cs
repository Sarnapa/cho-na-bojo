namespace ChoNaBojo.Server.Data.Entities;

/// <summary>
/// An app installation currently linked to the most recently authenticated user.
/// </summary>
public class PushInstallation
{
	/// <summary>Primary key; database-generated UUID.</summary>
	public Guid Id { get; set; }

	public Guid UserId { get; set; }

	public string DeviceRegistrationId { get; set; } = null!;

	public string? AppVersion { get; set; }

	public DateTime CreatedUtc { get; set; }

	public DateTime LastSeenUtc { get; set; }

	public DateTime? DisabledUtc { get; set; }

	public User User { get; set; } = null!;
}
