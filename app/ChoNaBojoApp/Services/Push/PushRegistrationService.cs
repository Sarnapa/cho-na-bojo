using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Validation;

namespace ChoNaBojo.App.Services.Push;

public sealed class PushRegistrationService : IPushRegistrationService
{
	#region Private fields
	private readonly IPushRegistrationStore _registrationStore;
	private readonly ISessionService _sessionService;
	private readonly IApiService _apiService;
	private readonly SemaphoreSlim _syncLock = new(1, 1);
	#endregion

	#region Constructors
	public PushRegistrationService(
		IPushRegistrationStore registrationStore,
		ISessionService sessionService,
		IApiService apiService)
	{
		_registrationStore = registrationStore;
		_sessionService = sessionService;
		_apiService = apiService;
	}
	#endregion

	#region Public methods
	public async Task OnRegistrationIdChangedAsync(
		string deviceRegistrationId,
		CancellationToken cancellationToken)
	{
		_registrationStore.SetLatestRegistrationId(deviceRegistrationId);
		await SyncAsync(cancellationToken);
	}

	public async Task SyncAsync(CancellationToken cancellationToken)
	{
		if (!_sessionService.IsAuthenticated)
		{
			return;
		}

		await _syncLock.WaitAsync(cancellationToken);
		try
		{
			AuthSession? session = _sessionService.Current;
			string? deviceRegistrationId = _registrationStore.GetLatestRegistrationId();
			if (session is null || string.IsNullOrWhiteSpace(deviceRegistrationId))
			{
				return;
			}

			var request = new RegisterPushInstallationRequest(
				deviceRegistrationId,
				AppInfo.Current.VersionString);
			if (!PushValidation.ValidateRegisterPushInstallationRequest(request).IsValid)
			{
				return;
			}

			CurrentUserResult currentUserResult =
				await _apiService.GetCurrentUserAsync(cancellationToken);
			Guid? userId = currentUserResult.Response?.UserId;
			if (currentUserResult.Status != CurrentUserResultStatus.Success
				|| userId is null
				|| userId == Guid.Empty)
			{
				return;
			}

			if (string.Equals(
					_registrationStore.GetUploadedRegistrationId(),
					deviceRegistrationId,
					StringComparison.Ordinal)
				&& _registrationStore.GetUploadedForUserId() == userId)
			{
				return;
			}

			RegisterPushInstallationResult result =
				await _apiService.RegisterPushInstallationAsync(request, cancellationToken);
			if (result.Status != RegisterPushInstallationResultStatus.Success
				|| result.Response is null
				|| _sessionService.Current?.RefreshToken != session.RefreshToken)
			{
				return;
			}

			_registrationStore.MarkUploaded(
				deviceRegistrationId,
				result.Response.InstallationId,
				userId.Value);
		}
		finally
		{
			_syncLock.Release();
		}
	}
	#endregion
}

public sealed class NoOpPushRegistrationService : IPushRegistrationService
{
	public Task SyncAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	public Task OnRegistrationIdChangedAsync(
		string deviceRegistrationId,
		CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
