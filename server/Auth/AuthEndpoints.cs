using Microsoft.EntityFrameworkCore;
using Npgsql;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.Data.Entities;
using ChoNaBojo.Validation;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Utils.Text;

namespace ChoNaBojo.Server.Auth;

public static class AuthEndpoints
{
	public const string RateLimiterPolicyName = "auth";

	public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var authGroup = endpoints.MapGroup("/auth")
			.RequireRateLimiting(RateLimiterPolicyName);

		authGroup.MapPost("/register", RegisterAsync)
			.AllowAnonymous()
			.WithName("AuthRegister");

		authGroup.MapPost("/login", LoginAsync)
			.AllowAnonymous()
			.WithName("AuthLogin");

		authGroup.MapPost("/refresh", RefreshAsync)
			.AllowAnonymous()
			.WithName("AuthRefresh");

		authGroup.MapPost("/logout", LogoutAsync)
			.AllowAnonymous()
			.WithName("AuthLogout");

		authGroup.MapGet("/me", (HttpContext httpContext) =>
			Results.Ok(new CurrentUserResponse(httpContext.GetUserId())))
			.RequireAuthorization()
			.WithName("AuthMe");

		return endpoints;
	}

	private static async Task<IResult> RegisterAsync(
		RegisterRequest request,
		ChoNaBojoContext dbContext,
		IPasswordService passwordService,
		IRefreshTokenService refreshTokenService,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var validation = AuthValidation.ValidateRegisterRequest(request);
		if (!validation.IsValid)
		{
			return ToValidationProblem(validation);
		}

		string loginEmail = TextNormalization.NormalizeOptionalText(request.LoginEmail)!;
		string normalizedLoginEmail = TextNormalization.NormalizeLoginEmail(request.LoginEmail);

		bool loginEmailExists = await dbContext.Users.AnyAsync(
			entity => entity.NormalizedLoginEmail == normalizedLoginEmail,
			cancellationToken);

		if (loginEmailExists)
		{
			return Results.Conflict(new { message = "An account with this login email already exists." });
		}

		var nowUtc = DateTime.UtcNow;
		var user = new User
		{
			LoginEmail = loginEmail,
			NormalizedLoginEmail = normalizedLoginEmail,
			PasswordHash = string.Empty,
			ContactPhone = TextNormalization.NormalizeOptionalText(request.ContactPhone),
			ContactEmail = TextNormalization.NormalizeOptionalText(request.ContactEmail),
			CommunicatorPlatform = request.CommunicatorPlatform,
			CommunicatorHandle = TextNormalization.NormalizeOptionalText(request.CommunicatorHandle),
			CreatedUtc = nowUtc,
			UpdatedUtc = nowUtc
		};

		user.PasswordHash = passwordService.Hash(user, request.Password);

		dbContext.Users.Add(user);
		try
		{
			await dbContext.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException exception) when (IsUniqueViolation(exception))
		{
			return Results.Conflict(new { message = "An account with this login email already exists." });
		}

		TokenPair pair = await refreshTokenService.IssueForLoginAsync(user, GetRequestIp(httpContext), cancellationToken);
		return Results.Ok(ToAuthResponse(pair));
	}

	private static async Task<IResult> LoginAsync(
		LoginRequest request,
		ChoNaBojoContext dbContext,
		IPasswordService passwordService,
		IRefreshTokenService refreshTokenService,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var validation = AuthValidation.ValidateLoginRequest(request);
		if (!validation.IsValid)
		{
			return ToValidationProblem(validation);
		}

		string normalizedLoginEmail = TextNormalization.NormalizeLoginEmail(request.LoginEmail);
		var user = await dbContext.Users.SingleOrDefaultAsync(
			entity => entity.NormalizedLoginEmail == normalizedLoginEmail,
			cancellationToken);

		if (user is null)
		{
			passwordService.PerformDummyVerification(request.Password);
			return Results.Unauthorized();
		}

		var verificationOutcome = passwordService.Verify(user, request.Password);
		if (verificationOutcome == PasswordVerificationOutcome.Failed)
		{
			return Results.Unauthorized();
		}

		if (verificationOutcome == PasswordVerificationOutcome.SuccessRehashNeeded)
		{
			user.PasswordHash = passwordService.Hash(user, request.Password);
			user.UpdatedUtc = DateTime.UtcNow;
			await dbContext.SaveChangesAsync(cancellationToken);
		}

		TokenPair pair = await refreshTokenService.IssueForLoginAsync(user, GetRequestIp(httpContext), cancellationToken);
		return Results.Ok(ToAuthResponse(pair));
	}

	private static async Task<IResult> RefreshAsync(
		RefreshRequest request,
		IRefreshTokenService refreshTokenService,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var validation = AuthValidation.ValidateRefreshRequest(request);
		if (!validation.IsValid)
		{
			return ToValidationProblem(validation);
		}

		RefreshTokenExchangeResult exchangeResult = await refreshTokenService.RotateAsync(
			request.RefreshToken,
			GetRequestIp(httpContext),
			cancellationToken);

		if (!exchangeResult.Succeeded || exchangeResult.Pair is null)
		{
			if (exchangeResult.Failure == RefreshTokenExchangeFailure.RetryInProgress)
			{
				return Results.Conflict(new { message = "Refresh already in progress; retry with your current token pair." });
			}

			return Results.Unauthorized();
		}

		return Results.Ok(ToAuthResponse(exchangeResult.Pair));
	}

	private static async Task<IResult> LogoutAsync(
		RefreshRequest request,
		IRefreshTokenService refreshTokenService,
		CancellationToken cancellationToken)
	{
		var validation = AuthValidation.ValidateRefreshRequest(request);
		if (!validation.IsValid)
		{
			return ToValidationProblem(validation);
		}

		await refreshTokenService.RevokeFamilyAsync(request.RefreshToken, cancellationToken);
		return Results.NoContent();
	}

	private static IResult ToValidationProblem(ValidationResult validation)
	{
		return Results.ValidationProblem(
			validation.Errors.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
	}

	private static AuthResponse ToAuthResponse(TokenPair pair)
	{
		return new AuthResponse(pair.AccessToken, pair.RefreshToken, pair.AccessTokenExpiresUtc);
	}

	private static string? GetRequestIp(HttpContext httpContext)
	{
		return httpContext.Connection.RemoteIpAddress?.ToString();
	}

	private static bool IsUniqueViolation(DbUpdateException exception)
	{
		return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
	}
}
