namespace ChoNaBojo.App.Services.Push;

public interface IPushRegistrationService
{
	Task SyncAsync(CancellationToken cancellationToken);

	Task OnRegistrationIdChangedAsync(
		string deviceRegistrationId,
		CancellationToken cancellationToken);
}
