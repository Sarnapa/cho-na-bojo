using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChoNaBojo.Server.Push;

public sealed class PushOutboxProcessor(
	ChoNaBojoContext dbContext,
	IPushGateway pushGateway,
	ILogger<PushOutboxProcessor> logger)
{
	#region Private constants
	private const int BatchSize = 20;
	#endregion

	#region Public methods
	public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
	{
		DateTime dueUtc = DateTime.UtcNow;
		List<Guid> candidateIds = await dbContext.PushOutbox
			.AsNoTracking()
			.Where(item =>
				item.CompletedUtc == null
				&& item.NextAttemptUtc <= dueUtc)
			.OrderBy(item => item.OccurredUtc)
			.Select(item => item.Id)
			.Take(BatchSize)
			.ToListAsync(cancellationToken);

		int processedCount = 0;
		foreach (Guid candidateId in candidateIds)
		{
			await using var transaction = await dbContext.Database.BeginTransactionAsync(
				IsolationLevel.ReadCommitted,
				cancellationToken);

			try
			{
				DateTime claimedUtc = DateTime.UtcNow;
				PushOutboxItem? outboxItem = await dbContext.PushOutbox
					.FromSqlInterpolated(
						$"""
							SELECT *
							FROM "PushOutbox"
							WHERE "Id" = {candidateId}
								AND "CompletedUtc" IS NULL
								AND "NextAttemptUtc" <= {claimedUtc}
							FOR UPDATE SKIP LOCKED
							""")
					.SingleOrDefaultAsync(cancellationToken);
				if (outboxItem is null)
				{
					await transaction.RollbackAsync(CancellationToken.None);
					continue;
				}

				await ProcessItemAsync(
					outboxItem,
					claimedUtc,
					cancellationToken);
				await dbContext.SaveChangesAsync(cancellationToken);
				await transaction.CommitAsync(cancellationToken);
				processedCount++;
			}
			catch (OperationCanceledException)
				when (cancellationToken.IsCancellationRequested)
			{
				await transaction.RollbackAsync(CancellationToken.None);
				throw;
			}
			catch (Exception exception)
			{
				await transaction.RollbackAsync(CancellationToken.None);
				logger.LogError(
					exception,
					"Push outbox item {OutboxId} failed; sibling items will continue.",
					candidateId);
			}
			finally
			{
				dbContext.ChangeTracker.Clear();
			}
		}

		return processedCount;
	}
	#endregion

	#region Private methods
	private async Task ProcessItemAsync(
		PushOutboxItem outboxItem,
		DateTime claimedUtc,
		CancellationToken cancellationToken)
	{
		outboxItem.ClaimedUtc = claimedUtc;
		double queueAgeMilliseconds =
			Math.Max(0, (claimedUtc - outboxItem.OccurredUtc).TotalMilliseconds);
		logger.LogInformation(
			"Claimed push outbox item {OutboxId} of type {NotificationType} with queue age {QueueAgeMilliseconds} ms.",
			outboxItem.Id,
			outboxItem.Type,
			queueAgeMilliseconds);

		List<PushDelivery> deliveries = await dbContext.PushDeliveries
			.Include(delivery => delivery.PushInstallation)
			.Where(delivery => delivery.PushOutboxItemId == outboxItem.Id)
			.ToListAsync(cancellationToken);

		if (deliveries.Count == 0)
		{
			List<PushInstallation> installations = await dbContext.PushInstallations
				.Where(installation =>
					installation.UserId == outboxItem.RecipientUserId
					&& installation.DisabledUtc == null)
				.ToListAsync(cancellationToken);

			deliveries = installations
				.Select(installation => new PushDelivery
				{
					PushOutboxItemId = outboxItem.Id,
					PushInstallationId = installation.Id,
					PushOutboxItem = outboxItem,
					PushInstallation = installation,
					NextAttemptUtc = claimedUtc
				})
				.ToList();

			dbContext.PushDeliveries.AddRange(deliveries);
			await dbContext.SaveChangesAsync(cancellationToken);
		}

		if (deliveries.Count == 0)
		{
			outboxItem.CompletedUtc = claimedUtc;
			logger.LogInformation(
				"Completed push outbox item {OutboxId} with no active installations.",
				outboxItem.Id);
			return;
		}

		foreach (PushDelivery delivery in deliveries.Where(delivery =>
			delivery.AcceptedUtc == null
			&& delivery.DeadLetteredUtc == null
			&& delivery.NextAttemptUtc <= claimedUtc))
		{
			await ProcessDeliveryAsync(
				outboxItem,
				delivery,
				cancellationToken);
		}

		List<PushDelivery> pendingDeliveries = deliveries
			.Where(delivery =>
				delivery.AcceptedUtc == null
				&& delivery.DeadLetteredUtc == null)
			.ToList();
		if (pendingDeliveries.Count == 0)
		{
			outboxItem.CompletedUtc = DateTime.UtcNow;
			return;
		}

		outboxItem.NextAttemptUtc = pendingDeliveries.Min(
			delivery => delivery.NextAttemptUtc);
	}

	private async Task ProcessDeliveryAsync(
		PushOutboxItem outboxItem,
		PushDelivery delivery,
		CancellationToken cancellationToken)
	{
		DateTime outcomeUtc = DateTime.UtcNow;
		if (delivery.PushInstallation.DisabledUtc is not null)
		{
			delivery.DeadLetteredUtc = outcomeUtc;
			delivery.LastErrorCode = "DestinationDisabled";
			return;
		}

		delivery.AttemptCount++;
		PushMessage message;
		try
		{
			message = PushPayloadFactory.Create(
				outboxItem,
				delivery.PushInstallation.DeviceRegistrationId,
				outcomeUtc);
		}
		catch (ArgumentException)
		{
			delivery.DeadLetteredUtc = outcomeUtc;
			delivery.LastErrorCode = "InvalidOutboxPayload";
			logger.LogWarning(
				"Dead-lettered push delivery {DeliveryId} for outbox {OutboxId}; type {NotificationType}, attempt {AttemptCount}, error {ErrorCode}.",
				delivery.Id,
				outboxItem.Id,
				outboxItem.Type,
				delivery.AttemptCount,
				delivery.LastErrorCode);
			return;
		}

		var stopwatch = Stopwatch.StartNew();
		PushSendOutcome outcome = await pushGateway.SendAsync(
			message,
			cancellationToken);
		stopwatch.Stop();
		outcomeUtc = DateTime.UtcNow;

		switch (outcome.Kind)
		{
			case PushSendResultKind.Accepted:
				delivery.AcceptedUtc = outcomeUtc;
				delivery.FcmMessageId = outcome.FcmMessageId;
				delivery.LastErrorCode = null;
				break;

			case PushSendResultKind.UnregisteredDestination:
				delivery.DeadLetteredUtc = outcomeUtc;
				delivery.LastErrorCode = outcome.ErrorCode;
				delivery.PushInstallation.DisabledUtc = outcomeUtc;
				break;

			case PushSendResultKind.TerminalFailure:
				delivery.DeadLetteredUtc = outcomeUtc;
				delivery.LastErrorCode = outcome.ErrorCode;
				break;

			case PushSendResultKind.RetryableFailure:
				delivery.LastErrorCode = outcome.ErrorCode;
				if (delivery.AttemptCount >= PushFailureClassifier.MaxAttempts)
				{
					delivery.DeadLetteredUtc = outcomeUtc;
				}
				else
				{
					TimeSpan retryDelay =
						PushFailureClassifier.CalculateRetryDelay(
							delivery.AttemptCount,
							outcome.RetryAfter,
							Random.Shared.NextDouble());
					delivery.NextAttemptUtc = outcomeUtc.Add(retryDelay);
				}
				break;

			default:
				throw new InvalidOperationException(
					$"Unsupported push send result kind '{outcome.Kind}'.");
		}

		logger.LogInformation(
			"Processed push delivery {DeliveryId} for outbox {OutboxId}; type {NotificationType}, destination hash {DestinationHash}, attempt {AttemptCount}, result {ResultKind}, duration {SendDurationMilliseconds} ms, FCM message {FcmMessageId}, error {ErrorCode}.",
			delivery.Id,
			outboxItem.Id,
			outboxItem.Type,
			HashDestination(delivery.PushInstallation.DeviceRegistrationId),
			delivery.AttemptCount,
			outcome.Kind,
			stopwatch.Elapsed.TotalMilliseconds,
			outcome.FcmMessageId,
			outcome.ErrorCode);
	}

	private static string HashDestination(string deviceRegistrationId)
	{
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(deviceRegistrationId));
		return Convert.ToHexString(hash)[..12];
	}
	#endregion
}
