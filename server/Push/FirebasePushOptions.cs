using System.ComponentModel.DataAnnotations;

namespace ChoNaBojo.Server.Push;

public sealed class FirebasePushOptions
{
	#region Public constants
	public const string SectionName = "Firebase";
	#endregion

	#region Public properties
	[Required]
	public string ProjectId { get; set; } = string.Empty;

	[Required]
	public string ServiceAccountJson { get; set; } = string.Empty;
	#endregion

	#region Overrides
	public override string ToString()
	{
		return $"{nameof(FirebasePushOptions)} {{ ProjectId = {ProjectId}, ServiceAccountJson = [REDACTED] }}";
	}
	#endregion
}
