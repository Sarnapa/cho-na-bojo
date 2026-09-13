using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ChoNaBojo.Server.IntegrationTests.Infrastructure;

internal static class TestJwtFactory
{
	#region Public methods
	public static string CreateToken(Guid userId)
	{
		string stableUserId = userId.ToString();
		return CreateToken(
			[
				new Claim(JwtRegisteredClaimNames.Sub, stableUserId),
				new Claim(ClaimTypes.NameIdentifier, stableUserId)
			],
			TestConfiguration.JwtSigningKey,
			DateTime.UtcNow.AddMinutes(15));
	}

	public static string CreateTokenWithoutStableIdentity()
	{
		return CreateToken(
			[],
			TestConfiguration.JwtSigningKey,
			DateTime.UtcNow.AddMinutes(15));
	}

	public static string CreateTokenWithInvalidStableIdentity()
	{
		return CreateToken(
			[
				new Claim(JwtRegisteredClaimNames.Sub, "not-a-guid"),
				new Claim(ClaimTypes.NameIdentifier, "also-not-a-guid")
			],
			TestConfiguration.JwtSigningKey,
			DateTime.UtcNow.AddMinutes(15));
	}

	public static string CreateTokenWithEmptyStableIdentity()
	{
		string emptyUserId = Guid.Empty.ToString();
		return CreateToken(
			[
				new Claim(JwtRegisteredClaimNames.Sub, emptyUserId),
				new Claim(ClaimTypes.NameIdentifier, emptyUserId)
			],
			TestConfiguration.JwtSigningKey,
			DateTime.UtcNow.AddMinutes(15));
	}

	public static string CreateExpiredToken(Guid userId)
	{
		string stableUserId = userId.ToString();
		return CreateToken(
			[
				new Claim(JwtRegisteredClaimNames.Sub, stableUserId),
				new Claim(ClaimTypes.NameIdentifier, stableUserId)
			],
			TestConfiguration.JwtSigningKey,
			DateTime.UtcNow.AddMinutes(-2),
			DateTime.UtcNow.AddMinutes(-17));
	}

	public static string CreateTokenWithInvalidSignature(Guid userId)
	{
		string stableUserId = userId.ToString();
		return CreateToken(
			[
				new Claim(JwtRegisteredClaimNames.Sub, stableUserId),
				new Claim(ClaimTypes.NameIdentifier, stableUserId)
			],
			"ChoNaBojo.IntegrationTests.InvalidSigningKey.2026",
			DateTime.UtcNow.AddMinutes(15));
	}
	#endregion

	#region Private methods
	private static string CreateToken(
		IEnumerable<Claim> identityClaims,
		string signingKey,
		DateTime expiresUtc,
		DateTime? issuedAtUtc = null)
	{
		DateTime issuedAt = issuedAtUtc ?? DateTime.UtcNow;
		var claims = identityClaims
			.Append(new Claim(
				JwtRegisteredClaimNames.Jti,
				Guid.NewGuid().ToString()))
			.ToArray();
		var descriptor = new SecurityTokenDescriptor
		{
			Issuer = TestConfiguration.JwtIssuer,
			Audience = TestConfiguration.JwtAudience,
			Subject = new ClaimsIdentity(claims),
			IssuedAt = issuedAt,
			NotBefore = issuedAt,
			Expires = expiresUtc,
			SigningCredentials = new SigningCredentials(
				new SymmetricSecurityKey(
					Encoding.UTF8.GetBytes(signingKey)),
				SecurityAlgorithms.HmacSha256)
		};

		return new JsonWebTokenHandler().CreateToken(descriptor);
	}
	#endregion
}
