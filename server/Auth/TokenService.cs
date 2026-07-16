using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ChoNaBojo.Server.Data.Entities;
using JwtRegisteredClaimNames = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames;

namespace ChoNaBojo.Server.Auth;

public interface ITokenService
{
	string CreateAccessToken(User user);
	DateTime GetAccessTokenExpiryUtc(DateTime issuedAtUtc);
}

/// <summary>
/// Issues short-lived access JWTs that carry only the stable user id identity.
/// </summary>
public class TokenService(IOptions<JwtOptions> jwtOptionsAccessor): ITokenService
{
	private readonly JsonWebTokenHandler _tokenHandler = new();
	private readonly JwtOptions _jwtOptions = jwtOptionsAccessor.Value;

	public string CreateAccessToken(User user)
	{
		var issuedAtUtc = DateTime.UtcNow;
		var userId = user.Id.ToString();
		var descriptor = new SecurityTokenDescriptor
		{
			Issuer = _jwtOptions.Issuer,
			Audience = _jwtOptions.Audience,
			Subject = new ClaimsIdentity(
				[
					new Claim(JwtRegisteredClaimNames.Sub, userId),
					new Claim(ClaimTypes.NameIdentifier, userId),
					new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
				]),
			IssuedAt = issuedAtUtc,
			NotBefore = issuedAtUtc,
			Expires = GetAccessTokenExpiryUtc(issuedAtUtc),
			SigningCredentials = new SigningCredentials(
				new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SigningKey)),
				SecurityAlgorithms.HmacSha256)
		};

		return _tokenHandler.CreateToken(descriptor);
	}

	public DateTime GetAccessTokenExpiryUtc(DateTime issuedAtUtc)
	{
		return issuedAtUtc.AddMinutes(_jwtOptions.AccessTokenMinutes);
	}
}
