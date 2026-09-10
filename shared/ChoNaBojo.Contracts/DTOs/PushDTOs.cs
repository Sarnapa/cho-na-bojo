namespace ChoNaBojo.Contracts.DTOs;

#region Requests DTOs
public sealed record RegisterPushInstallationRequest(
	string DeviceRegistrationId,
	string? AppVersion);
#endregion

#region Responses DTOs
public sealed record RegisterPushInstallationResponse(Guid InstallationId);
#endregion
