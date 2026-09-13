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
			]);
	}

	public static string CreateTokenWithoutStableIdentity()
	{
		return CreateToken([]);
	}

	public static string CreateTokenWithInvalidStableIdentity()
	{
		return CreateToken(
			[
				new Claim(JwtRegisteredClaimNames.Sub, "not-a-guid"),
				new Claim(ClaimTypes.NameIdentifier, "also-not-a-guid")
			]);
	}

	public static string CreateTokenWithEmptyStableIdentity()
	{
		string emptyUserId = Guid.Empty.ToString();
		return CreateToken(
			[
				new Claim(JwtRegisteredClaimNames.Sub, emptyUserId),
				new Claim(ClaimTypes.NameIdentifier, emptyUserId)
			]);
	}
	#endregion

	#region Private methods
	private static string CreateToken(IEnumerable<Claim> identityClaims)
	{
		DateTime issuedAtUtc = DateTime.UtcNow;
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
			IssuedAt = issuedAtUtc,
			NotBefore = issuedAtUtc,
			Expires = issuedAtUtc.AddMinutes(15),
			SigningCredentials = new SigningCredentials(
				new SymmetricSecurityKey(
					Encoding.UTF8.GetBytes(TestConfiguration.JwtSigningKey)),
				SecurityAlgorithms.HmacSha256)
		};

		return new JsonWebTokenHandler().CreateToken(descriptor);
	}
	#endregion
}
