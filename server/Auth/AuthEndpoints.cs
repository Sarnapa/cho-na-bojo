using Microsoft.EntityFrameworkCore;
using Npgsql;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.Data.Entities;

namespace ChoNaBojo.Server.Auth;

public static class AuthEndpoints
{
	public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var authGroup = endpoints.MapGroup("/auth");

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
		var validationErrors = ValidateRegisterRequest(request);
		if (validationErrors is not null)
		{
			return Results.ValidationProblem(validationErrors);
		}

		string loginEmail = AuthNormalization.NormalizeOptionalText(request.LoginEmail)!;
		string normalizedLoginEmail = AuthNormalization.NormalizeLoginEmail(loginEmail);

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
			ContactPhone = AuthNormalization.NormalizeOptionalText(request.ContactPhone),
			ContactEmail = AuthNormalization.NormalizeOptionalText(request.ContactEmail),
			CommunicatorPlatform = request.CommunicatorPlatform,
			CommunicatorHandle = AuthNormalization.NormalizeOptionalText(request.CommunicatorHandle),
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
		var validationErrors = ValidateLoginRequest(request);
		if (validationErrors is not null)
		{
			return Results.ValidationProblem(validationErrors);
		}

		string normalizedLoginEmail = AuthNormalization.NormalizeLoginEmail(request.LoginEmail);
		var user = await dbContext.Users.SingleOrDefaultAsync(
			entity => entity.NormalizedLoginEmail == normalizedLoginEmail,
			cancellationToken);

		if (user is null)
		{
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
		var validationErrors = ValidateRefreshRequest(request);
		if (validationErrors is not null)
		{
			return Results.ValidationProblem(validationErrors);
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
		var validationErrors = ValidateRefreshRequest(request);
		if (validationErrors is not null)
		{
			return Results.ValidationProblem(validationErrors);
		}

		await refreshTokenService.RevokeFamilyAsync(request.RefreshToken, cancellationToken);
		return Results.NoContent();
	}

	private static Dictionary<string, string[]>? ValidateRegisterRequest(RegisterRequest request)
	{
		var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
		string? loginEmail = AuthNormalization.NormalizeOptionalText(request.LoginEmail);

		if (loginEmail is null || !AuthNormalization.IsBasicEmailShape(loginEmail))
		{
			AddValidationError(errors, "loginEmail", "Login email must have a basic single-@ email shape.");
		}

		if (string.IsNullOrEmpty(request.Password) || request.Password.Length is < 8 or > 128)
		{
			AddValidationError(errors, "password", "Password must be between 8 and 128 characters.");
		}

		string? contactPhone = AuthNormalization.NormalizeOptionalText(request.ContactPhone);
		string? contactEmail = AuthNormalization.NormalizeOptionalText(request.ContactEmail);
		string? communicatorHandle = AuthNormalization.NormalizeOptionalText(request.CommunicatorHandle);
		bool hasCommunicatorPlatform = request.CommunicatorPlatform.HasValue;
		bool hasCommunicatorHandle = communicatorHandle is not null;

		if (hasCommunicatorPlatform != hasCommunicatorHandle)
		{
			AddValidationError(
				errors,
				"communicator",
				"Communicator platform and communicator handle must be provided together.");
		}

		bool hasAnyContactMethod = contactPhone is not null
			|| contactEmail is not null
			|| (hasCommunicatorPlatform && hasCommunicatorHandle);
		if (!hasAnyContactMethod)
		{
			AddValidationError(
				errors,
				"contact",
				"At least one contact method is required: phone, contact email, or communicator platform plus handle.");
		}

		return errors.Count == 0 ? null : errors;
	}

	private static Dictionary<string, string[]>? ValidateLoginRequest(LoginRequest request)
	{
		var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
		string? loginEmail = AuthNormalization.NormalizeOptionalText(request.LoginEmail);

		if (loginEmail is null || !AuthNormalization.IsBasicEmailShape(loginEmail))
		{
			AddValidationError(errors, "loginEmail", "Login email must have a basic single-@ email shape.");
		}

		if (string.IsNullOrEmpty(request.Password))
		{
			AddValidationError(errors, "password", "Password is required.");
		}

		return errors.Count == 0 ? null : errors;
	}

	private static Dictionary<string, string[]>? ValidateRefreshRequest(RefreshRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.RefreshToken))
		{
			return new Dictionary<string, string[]>(StringComparer.Ordinal)
			{
				["refreshToken"] = ["Refresh token is required."]
			};
		}

		return null;
	}

	private static void AddValidationError(IDictionary<string, string[]> errors, string key, string message)
	{
		if (errors.TryGetValue(key, out string[]? existing))
		{
			errors[key] = [.. existing, message];
			return;
		}

		errors[key] = [message];
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
