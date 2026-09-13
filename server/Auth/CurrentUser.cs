using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ChoNaBojo.Server.Auth;

public static class CurrentUser
{
	#region Public methods
	/// <summary>
	/// Canonical way to extract the authenticated caller's stable user id from claims.
	/// </summary>
	public static Guid GetUserId(this ClaimsPrincipal principal)
	{
		if (principal.TryGetUserId(out Guid userId))
		{
			return userId;
		}

		bool hasIdentityClaim = principal.Claims.Any(claim =>
			claim.Type is JwtRegisteredClaimNames.Sub or ClaimTypes.NameIdentifier
				&& !string.IsNullOrWhiteSpace(claim.Value));
		if (!hasIdentityClaim)
		{
			throw new InvalidOperationException("Authenticated user id claim is missing.");
		}

		throw new InvalidOperationException("Authenticated user id claim is invalid.");
	}

	public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
	{
		foreach (string claimType in new[]
			{
				JwtRegisteredClaimNames.Sub,
				ClaimTypes.NameIdentifier
			})
		{
			foreach (Claim claim in principal.FindAll(claimType))
			{
				if (Guid.TryParse(claim.Value, out userId)
					&& userId != Guid.Empty)
				{
					return true;
				}
			}
		}

		userId = Guid.Empty;
		return false;
	}

	public static Guid GetUserId(this HttpContext httpContext)
	{
		return httpContext.User.GetUserId();
	}
	#endregion
}
