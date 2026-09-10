using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.App.Services.Push;

public enum RegisterPushInstallationResultStatus
{
	Success,
	ValidationFailed,
	Unauthorized,
	Network,
	Unknown
}

public sealed record RegisterPushInstallationResult
{
	#region Properties
	public RegisterPushInstallationResultStatus Status { get; }
	public RegisterPushInstallationResponse? Response { get; }
	public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }
	#endregion

	#region Constructors
	private RegisterPushInstallationResult(
		RegisterPushInstallationResultStatus status,
		RegisterPushInstallationResponse? response,
		IReadOnlyDictionary<string, string[]>? validationErrors)
	{
		Status = status;
		Response = response;
		ValidationErrors = validationErrors;
	}
	#endregion

	#region Public methods
	public static RegisterPushInstallationResult Success(
		RegisterPushInstallationResponse response)
	{
		return new(RegisterPushInstallationResultStatus.Success, response, null);
	}

	public static RegisterPushInstallationResult ValidationFailed(
		IReadOnlyDictionary<string, string[]> errors)
	{
		return new(RegisterPushInstallationResultStatus.ValidationFailed, null, errors);
	}

	public static RegisterPushInstallationResult Unauthorized()
	{
		return new(RegisterPushInstallationResultStatus.Unauthorized, null, null);
	}

	public static RegisterPushInstallationResult Network()
	{
		return new(RegisterPushInstallationResultStatus.Network, null, null);
	}

	public static RegisterPushInstallationResult Unknown()
	{
		return new(RegisterPushInstallationResultStatus.Unknown, null, null);
	}
	#endregion
}
