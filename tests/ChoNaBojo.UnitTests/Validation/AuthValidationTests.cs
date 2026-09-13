using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Contracts.Enums;
using ChoNaBojo.Validation;

namespace ChoNaBojo.UnitTests.Validation;

public sealed class AuthValidationTests
{
	#region Test methods
	[Theory]
	[InlineData(" +48 123 456 789 ", null, null, null)]
	[InlineData(null, " contact@example.com ", null, null)]
	[InlineData(null, null, CommunicatorPlatform.Messenger, " player.one ")]
	[InlineData(null, null, CommunicatorPlatform.Instagram, " player_two ")]
	[InlineData(null, null, CommunicatorPlatform.WhatsApp, " +48123456789 ")]
	public void Registration_accepts_each_supported_contact_method(
		string? phone,
		string? contactEmail,
		CommunicatorPlatform? platform,
		string? handle)
	{
		RegisterRequest request = CreateRequest(phone, contactEmail, platform, handle);

		ValidationResult result = AuthValidation.ValidateRegisterRequest(request);

		Assert.True(result.IsValid);
	}

	[Fact]
	public void Registration_rejects_missing_contact_methods()
	{
		RegisterRequest request = CreateRequest(null, null, null, null);

		ValidationResult result = AuthValidation.ValidateRegisterRequest(request);

		Assert.False(result.IsValid);
		Assert.Contains("contact", result.Errors.Keys);
	}

	[Theory]
	[InlineData(CommunicatorPlatform.Messenger, null)]
	[InlineData(null, "player")]
	public void Registration_rejects_unpaired_communicator_values(
		CommunicatorPlatform? platform,
		string? handle)
	{
		RegisterRequest request = CreateRequest(null, null, platform, handle);

		ValidationResult result = AuthValidation.ValidateRegisterRequest(request);

		Assert.False(result.IsValid);
		Assert.Contains("communicator", result.Errors.Keys);
	}

	[Fact]
	public void Registration_rejects_undefined_communicator_platform()
	{
		RegisterRequest request = CreateRequest(
			null,
			null,
			(CommunicatorPlatform)99,
			"player");

		ValidationResult result = AuthValidation.ValidateRegisterRequest(request);

		Assert.False(result.IsValid);
		Assert.Contains("communicatorPlatform", result.Errors.Keys);
	}

	[Fact]
	public void Whitespace_only_contact_values_are_treated_as_missing()
	{
		RegisterRequest request = CreateRequest(" \t ", "\r\n", null, "  ");

		ValidationResult result = AuthValidation.ValidateRegisterRequest(request);

		Assert.False(result.IsValid);
		Assert.Contains("contact", result.Errors.Keys);
	}
	#endregion

	#region Private methods
	private static RegisterRequest CreateRequest(
		string? phone,
		string? contactEmail,
		CommunicatorPlatform? platform,
		string? handle)
	{
		return new RegisterRequest(
			"login@example.com",
			"StrongPass1",
			phone,
			contactEmail,
			platform,
			handle);
	}
	#endregion
}
