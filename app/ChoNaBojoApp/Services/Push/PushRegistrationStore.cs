namespace ChoNaBojo.App.Services.Push;

public sealed class PushRegistrationStore : IPushRegistrationStore
{
	#region Private constants
	private const string LatestRegistrationIdKey = "push_latest_registration_id";
	private const string UploadedRegistrationIdKey = "push_uploaded_registration_id";
	private const string InstallationIdKey = "push_installation_id";
	private const string UploadedForUserIdKey = "push_uploaded_for_user_id";
	#endregion

	#region Public methods
	public string? GetLatestRegistrationId()
	{
		return GetOptionalString(LatestRegistrationIdKey);
	}

	public void SetLatestRegistrationId(string deviceRegistrationId)
	{
		Preferences.Default.Set(LatestRegistrationIdKey, deviceRegistrationId);
	}

	public string? GetUploadedRegistrationId()
	{
		return GetOptionalString(UploadedRegistrationIdKey);
	}

	public Guid? GetInstallationId()
	{
		return GetGuid(InstallationIdKey);
	}

	public Guid? GetUploadedForUserId()
	{
		return GetGuid(UploadedForUserIdKey);
	}

	public void MarkUploaded(
		string deviceRegistrationId,
		Guid installationId,
		Guid userId)
	{
		Preferences.Default.Set(UploadedRegistrationIdKey, deviceRegistrationId);
		Preferences.Default.Set(InstallationIdKey, installationId.ToString("D"));
		Preferences.Default.Set(UploadedForUserIdKey, userId.ToString("D"));
	}

	public void ClearUploadedState()
	{
		Preferences.Default.Remove(UploadedRegistrationIdKey);
		Preferences.Default.Remove(InstallationIdKey);
		Preferences.Default.Remove(UploadedForUserIdKey);
	}
	#endregion

	#region Private methods
	private static string? GetOptionalString(string key)
	{
		string value = Preferences.Default.Get(key, string.Empty);
		return string.IsNullOrEmpty(value) ? null : value;
	}

	private static Guid? GetGuid(string key)
	{
		return Guid.TryParse(GetOptionalString(key), out Guid value)
			? value
			: null;
	}
	#endregion
}

public sealed class NoOpPushRegistrationStore : IPushRegistrationStore
{
	public string? GetLatestRegistrationId()
	{
		return null;
	}

	public void SetLatestRegistrationId(string deviceRegistrationId)
	{
	}

	public string? GetUploadedRegistrationId()
	{
		return null;
	}

	public Guid? GetInstallationId()
	{
		return null;
	}

	public Guid? GetUploadedForUserId()
	{
		return null;
	}

	public void MarkUploaded(
		string deviceRegistrationId,
		Guid installationId,
		Guid userId)
	{
	}

	public void ClearUploadedState()
	{
	}
}
