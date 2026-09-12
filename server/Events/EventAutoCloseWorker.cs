using ChoNaBojo.Contracts.Enums;
using ChoNaBojo.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace ChoNaBojo.Server.Events;

public sealed class EventAutoCloseWorker(
	IServiceScopeFactory scopeFactory,
	ILogger<EventAutoCloseWorker> logger) : BackgroundService
{
	#region Private fields
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
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
				var dbContext =
					scope.ServiceProvider.GetRequiredService<ChoNaBojoContext>();
				DateTime closedUtc = DateTime.UtcNow;

				int closedEventCount = await dbContext.SportsEvents
					.Where(sportsEvent =>
						sportsEvent.Status == EventStatus.Active
							&& sportsEvent.EstimatedEndsAtUtc <= closedUtc)
					.ExecuteUpdateAsync(
						setters => setters
							.SetProperty(
								sportsEvent => sportsEvent.Status,
								EventStatus.Closed)
							.SetProperty(
								sportsEvent => sportsEvent.StatusChangedUtc,
								closedUtc),
						stoppingToken);

				if (closedEventCount > 0)
				{
					logger.LogInformation(
						"Auto-closed {ClosedEventCount} finished event(s).",
						closedEventCount);
				}
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception exception)
			{
				logger.LogError(
					exception,
					"Event auto-close worker iteration failed; processing will retry.");
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
