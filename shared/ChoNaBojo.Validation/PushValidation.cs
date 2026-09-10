using ChoNaBojo.Contracts.Consts;
using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.Validation;

/// <summary>
/// Framework-neutral validation for push installation registration.
/// </summary>
public static class PushValidation
{
	#region Public methods
	public static ValidationResult ValidateRegisterPushInstallationRequest(
		RegisterPushInstallationRequest request)
	{
		var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

		if (string.IsNullOrWhiteSpace(request.DeviceRegistrationId))
		{
			errors["deviceRegistrationId"] = ["Device registration id is required."];
		}
		else if (request.DeviceRegistrationId.Length > PushPolicy.DeviceRegistrationIdMaxLength)
		{
			errors["deviceRegistrationId"] =
			[
				$"Device registration id must not exceed {PushPolicy.DeviceRegistrationIdMaxLength} characters."
			];
		}

		if (request.AppVersion?.Length > PushPolicy.AppVersionMaxLength)
		{
			errors["appVersion"] =
			[
				$"App version must not exceed {PushPolicy.AppVersionMaxLength} characters."
			];
		}

		return errors.Count == 0 ? ValidationResult.Valid : new ValidationResult(errors);
	}
	#endregion
}
