using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ChoNaBojo.Server.Auth;

public static class CurrentUser
{
	/// <summary>
	/// Canonical way to extract the authenticated caller's stable user id from claims.
	/// </summary>
	public static Guid GetUserId(this ClaimsPrincipal principal)
	{
		string? userIdClaim = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
			?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

		if (string.IsNullOrWhiteSpace(userIdClaim))
		{
			throw new InvalidOperationException("Authenticated user id claim is missing.");
		}

		if (!Guid.TryParse(userIdClaim, out var userId))
		{
			throw new InvalidOperationException("Authenticated user id claim is invalid.");
		}

		return userId;
	}

	public static Guid GetUserId(this HttpContext httpContext)
	{
		return httpContext.User.GetUserId();
	}
}
