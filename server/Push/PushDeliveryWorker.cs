namespace ChoNaBojo.Server.Push;

public sealed class PushDeliveryWorker(
	IServiceScopeFactory scopeFactory,
	ILogger<PushDeliveryWorker> logger) : BackgroundService
{
	#region Private fields
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
	#endregion

	#region BackgroundService implementation
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await using AsyncServiceScope scope =
					scopeFactory.CreateAsyncScope();
				var processor =
					scope.ServiceProvider.GetRequiredService<PushOutboxProcessor>();
				await processor.ProcessDueAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception exception)
			{
				logger.LogError(
					exception,
					"Push delivery worker iteration failed; processing will retry.");
			}

			try
			{
				await Task.Delay(PollInterval, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
		}
	}
	#endregion
}
