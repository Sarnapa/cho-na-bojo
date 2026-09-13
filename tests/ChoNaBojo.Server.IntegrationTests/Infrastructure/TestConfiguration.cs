namespace ChoNaBojo.Server.IntegrationTests.Infrastructure;

internal static class TestConfiguration
{
	public const string JwtIssuer = "ChoNaBojo.IntegrationTests";
	public const string JwtAudience = "ChoNaBojo.IntegrationTests.Client";
	public const string JwtSigningKey =
		"ChoNaBojo.IntegrationTests.SigningKey.Isolation.2026";

	public static IReadOnlyDictionary<string, string?> Create(
		string connectionString)
	{
		return new Dictionary<string, string?>
		{
			["ConnectionStrings:AppDb"] = connectionString,
			["Jwt:Issuer"] = JwtIssuer,
			["Jwt:Audience"] = JwtAudience,
			["Jwt:SigningKey"] = JwtSigningKey,
			["Jwt:AccessTokenMinutes"] = "15",
			["Jwt:RefreshTokenDays"] = "30",
			["Firebase:ProjectId"] = "chonabojo-integration-tests",
			["Firebase:ServiceAccountJson"] = "{}"
		};
	}
}
