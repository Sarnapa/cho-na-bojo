namespace ChoNaBojo.Server.Push;

public interface IPushGateway
{
	Task<PushSendOutcome> SendAsync(
		PushMessage message,
		CancellationToken cancellationToken);
}
