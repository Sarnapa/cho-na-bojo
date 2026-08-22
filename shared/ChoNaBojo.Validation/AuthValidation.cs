using ChoNaBojo.Contracts.Consts;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Utils.Text;

namespace ChoNaBojo.Validation;

/// <summary>
/// Pure validation for the auth contracts. Shared by the API (authoritative) and the
/// MAUI client (instant UX feedback). Returns a <see cref="ValidationResult"/>, never an
/// ASP.NET <c>IResult</c>, so it stays framework-neutral.
/// </summary>
public static class AuthValidation
{
	#region Public methods
	public static ValidationResult ValidateRegisterRequest(RegisterRequest request)
	{
		var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
		string? loginEmail = TextNormalization.NormalizeOptionalText(request.LoginEmail);

		if (loginEmail is null || !TextNormalization.IsBasicEmailShape(loginEmail))
		{
			AddValidationError(errors, "loginEmail", "Login email must have a basic single-@ email shape.");
		}

		if (string.IsNullOrEmpty(request.Password)
			|| request.Password.Length < PasswordPolicy.MinLength
			|| request.Password.Length > PasswordPolicy.MaxLength)
		{
			AddValidationError(
				errors,
				"password",
				$"Password must be between {PasswordPolicy.MinLength} and {PasswordPolicy.MaxLength} characters.");
		}

		string? contactPhone = TextNormalization.NormalizeOptionalText(request.ContactPhone);
		string? contactEmail = TextNormalization.NormalizeOptionalText(request.ContactEmail);
		string? communicatorHandle = TextNormalization.NormalizeOptionalText(request.CommunicatorHandle);
		bool hasCommunicatorPlatform = request.CommunicatorPlatform.HasValue;
		bool hasCommunicatorHandle = communicatorHandle is not null;

		if (hasCommunicatorPlatform != hasCommunicatorHandle)
		{
			AddValidationError(
				errors,
				"communicator",
				"Communicator platform and communicator login must be provided together.");
		}

		if (hasCommunicatorPlatform && !Enum.IsDefined(request.CommunicatorPlatform!.Value))
		{
			AddValidationError(
				errors,
				"communicatorPlatform",
				"Communicator platform is not a supported value.");
		}

		bool hasAnyContactMethod = contactPhone is not null
			|| contactEmail is not null
			|| (hasCommunicatorPlatform && hasCommunicatorHandle);
		if (!hasAnyContactMethod)
		{
			AddValidationError(
				errors,
				"contact",
				"At least one contact method is required: phone, contact email, or communicator platform plus login.");
		}

		return errors.Count == 0 ? ValidationResult.Valid : new ValidationResult(errors);
	}

	public static ValidationResult ValidateLoginRequest(LoginRequest request)
	{
		var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
		string? loginEmail = TextNormalization.NormalizeOptionalText(request.LoginEmail);

		if (loginEmail is null || !TextNormalization.IsBasicEmailShape(loginEmail))
		{
			AddValidationError(errors, "loginEmail", "Login email must have a basic single-@ email shape.");
		}

		if (string.IsNullOrEmpty(request.Password))
		{
			AddValidationError(errors, "password", "Password is required.");
		}

		return errors.Count == 0 ? ValidationResult.Valid : new ValidationResult(errors);
	}

	public static ValidationResult ValidateRefreshRequest(RefreshRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.RefreshToken))
		{
			return new ValidationResult(new Dictionary<string, string[]>(StringComparer.Ordinal)
			{
				["refreshToken"] = ["Refresh token is required."]
			});
		}

		return ValidationResult.Valid;
	}
	#endregion

	#region Private methods
	private static void AddValidationError(IDictionary<string, string[]> errors, string key, string message)
	{
		if (errors.TryGetValue(key, out string[]? existing))
		{
			errors[key] = [.. existing, message];
			return;
		}

		errors[key] = [message];
	}
	#endregion
}
