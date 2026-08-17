namespace ChoNaBojo.App.Services.Auth;

#region AuthResultStatus
public enum AuthResultStatus
{
	Success,
	ValidationFailed,
	Unauthorized,
	Conflict,
	Network,
	Unknown
}
#endregion

#region AuthResult
/// <summary>
/// Client-side result of a register/login call. Never leaks a raw <see cref="HttpResponseMessage"/>
/// to ViewModels — every outcome the server can return is represented explicitly.
/// </summary>
public sealed record AuthResult
{
	#region Properties
	public AuthResultStatus Status { get; }
	public ChoNaBojo.Contracts.DTOs.AuthResponse? Response { get; }
	public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }
	public string? Message { get; }
	#endregion

	#region Constructors
	private AuthResult(
		AuthResultStatus status,
		ChoNaBojo.Contracts.DTOs.AuthResponse? response,
		IReadOnlyDictionary<string, string[]>? validationErrors,
		string? message)
	{
		Status = status;
		Response = response;
		ValidationErrors = validationErrors;
		Message = message;
	}
	#endregion

	#region Public methods
	public static AuthResult Success(ChoNaBojo.Contracts.DTOs.AuthResponse response)
	{
		return new(AuthResultStatus.Success, response, null, null);
	}

	public static AuthResult ValidationFailed(IReadOnlyDictionary<string, string[]> errors)
	{
		return new(AuthResultStatus.ValidationFailed, null, errors, null);
	}

	public static AuthResult Unauthorized()
	{
		return new(AuthResultStatus.Unauthorized, null, null, null);
	}

	public static AuthResult Conflict(string message)
	{
		return new(AuthResultStatus.Conflict, null, null, message);
	}

	public static AuthResult Network()
	{
		return new(AuthResultStatus.Network, null, null, null);
	}

	public static AuthResult Unknown()
	{
		return new(AuthResultStatus.Unknown, null, null, null);
	}
	#endregion
}
#endregion

#region CurrentUserResultStatus
public enum CurrentUserResultStatus
{
	Success,
	Unauthorized,
	Network,
	Unknown
}
#endregion

#region CurrentUserResult
/// <summary>Client-side result of the protected <c>GET /auth/me</c> call.</summary>
public sealed record CurrentUserResult
{
	#region Properties
	public CurrentUserResultStatus Status { get; }
	public ChoNaBojo.Contracts.DTOs.CurrentUserResponse? Response { get; }
	#endregion
	
	#region Constructors
	private CurrentUserResult(CurrentUserResultStatus status, ChoNaBojo.Contracts.DTOs.CurrentUserResponse? response)
	{
		Status = status;
		Response = response;
	}
	#endregion

	#region Public methods
	public static CurrentUserResult Success(ChoNaBojo.Contracts.DTOs.CurrentUserResponse response)
	{
		return new(CurrentUserResultStatus.Success, response);
	}

	public static CurrentUserResult Unauthorized()
	{
		return new(CurrentUserResultStatus.Unauthorized, null);
	}

	public static CurrentUserResult Network()
	{
		return new(CurrentUserResultStatus.Network, null);
	}

	public static CurrentUserResult Unknown()
	{
		return new(CurrentUserResultStatus.Unknown, null);
	}
	#endregion
}
#endregion

#region ValidationProblemResponse
/// <summary>Lightweight shape for the server's RFC-7807 <c>ValidationProblem</c> body.</summary>
public sealed record ValidationProblemResponse(Dictionary<string, string[]> Errors);
#endregion
