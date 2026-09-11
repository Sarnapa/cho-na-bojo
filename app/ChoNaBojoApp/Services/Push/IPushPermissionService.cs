namespace ChoNaBojo.App.Services.Push;

public interface IPushPermissionService
{
	Task RequestIfNeededAsync(CancellationToken cancellationToken);
}
