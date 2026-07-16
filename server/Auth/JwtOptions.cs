using System.ComponentModel.DataAnnotations;

namespace ChoNaBojo.Server.Auth;

/// <summary>
/// JWT settings used consistently for token issuance and validation.
/// </summary>
public class JwtOptions
{
	public const string SectionName = "Jwt";

	[Required]
	public string Issuer { get; set; } = string.Empty;

	[Required]
	public string Audience { get; set; } = string.Empty;

	[Required]
	public string SigningKey { get; set; } = string.Empty;

	[Range(1, 1440)]
	public int AccessTokenMinutes { get; set; } = 15;

	[Range(1, 365)]
	public int RefreshTokenDays { get; set; } = 30;
}
