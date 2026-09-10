namespace ChoNaBojo.App.Services.Push;

public interface IPushRegistrationStore
{
	string? GetLatestRegistrationId();

	void SetLatestRegistrationId(string deviceRegistrationId);

	string? GetUploadedRegistrationId();

	Guid? GetInstallationId();

	Guid? GetUploadedForUserId();

	void MarkUploaded(
		string deviceRegistrationId,
		Guid installationId,
		Guid userId);

	void ClearUploadedState();
}
