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

	/// <summary>
	/// Keeps a claimed item out of the due set while its sends run outside the
	/// claim transaction, so a second worker cannot re-claim and double-send it.
	/// </summary>
	private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);
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
			try
			{
				ClaimedOutboxItem? claim = await ClaimItemAsync(
					candidateId,
					cancellationToken);
				if (claim is null)
				{
					continue;
				}

				await ProcessItemAsync(claim, cancellationToken);
				processedCount++;
			}
			catch (OperationCanceledException)
				when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception exception)
			{
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
	/// <summary>
	/// Claims the item and materializes its deliveries in one short transaction,
	/// leasing the item so that no network round-trip is made under the row lock.
	/// </summary>
	private async Task<ClaimedOutboxItem?> ClaimItemAsync(
		Guid candidateId,
		CancellationToken cancellationToken)
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
				return null;
			}

			outboxItem.ClaimedUtc = claimedUtc;
			outboxItem.NextAttemptUtc = claimedUtc.Add(ClaimLease);

			double queueAgeMilliseconds =
				Math.Max(0, (claimedUtc - outboxItem.OccurredUtc).TotalMilliseconds);
			logger.LogInformation(
				"Claimed push outbox item {OutboxId} of type {NotificationType} with queue age {QueueAgeMilliseconds} ms.",
				outboxItem.Id,
				outboxItem.Type,
				queueAgeMilliseconds);

			List<PushDelivery> deliveries = await LoadOrCreateDeliveriesAsync(
				outboxItem,
				claimedUtc,
				cancellationToken);

			await dbContext.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);

			return new ClaimedOutboxItem(outboxItem, deliveries, claimedUtc);
		}
		catch
		{
			await transaction.RollbackAsync(CancellationToken.None);
			throw;
		}
	}

	private async Task<List<PushDelivery>> LoadOrCreateDeliveriesAsync(
		PushOutboxItem outboxItem,
		DateTime claimedUtc,
		CancellationToken cancellationToken)
	{
		List<PushDelivery> deliveries = await dbContext.PushDeliveries
			.Include(delivery => delivery.PushInstallation)
			.Where(delivery => delivery.PushOutboxItemId == outboxItem.Id)
			.ToListAsync(cancellationToken);

		if (deliveries.Count > 0)
		{
			return deliveries;
		}

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
		return deliveries;
	}

	/// <summary>
	/// Sends the due deliveries outside any transaction, persisting each outcome on
	/// its own so an accepted send is durable the moment FCM accepts it.
	/// </summary>
	private async Task ProcessItemAsync(
		ClaimedOutboxItem claim,
		CancellationToken cancellationToken)
	{
		foreach (PushDelivery delivery in claim.Deliveries.Where(delivery =>
			delivery.AcceptedUtc == null
			&& delivery.DeadLetteredUtc == null
			&& delivery.NextAttemptUtc <= claim.ClaimedUtc))
		{
			await ProcessDeliveryAsync(
				claim.Item,
				delivery,
				cancellationToken);
			await dbContext.SaveChangesAsync(CancellationToken.None);
		}

		await FinalizeItemAsync(claim);
	}

	private async Task FinalizeItemAsync(ClaimedOutboxItem claim)
	{
		List<PushDelivery> pendingDeliveries = claim.Deliveries
			.Where(delivery =>
				delivery.AcceptedUtc == null
				&& delivery.DeadLetteredUtc == null)
			.ToList();

		if (pendingDeliveries.Count == 0)
		{
			claim.Item.CompletedUtc = DateTime.UtcNow;
			if (claim.Deliveries.Count == 0)
			{
				logger.LogInformation(
					"Completed push outbox item {OutboxId} with no active installations.",
					claim.Item.Id);
			}
		}
		else
		{
			claim.Item.NextAttemptUtc = pendingDeliveries.Min(
				delivery => delivery.NextAttemptUtc);
		}

		await dbContext.SaveChangesAsync(CancellationToken.None);
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
		PushSendOutcome outcome;
		try
		{
			outcome = await pushGateway.SendAsync(
				message,
				cancellationToken);
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			logger.LogError(
				exception,
				"Unexpected failure sending push delivery {DeliveryId} for outbox {OutboxId}; retrying under the standard backoff.",
				delivery.Id,
				outboxItem.Id);
			outcome = PushSendOutcome.Retryable("UnexpectedSendFailure", null);
		}
		finally
		{
			stopwatch.Stop();
		}

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

	#region ClaimedOutboxItem record
	private sealed record ClaimedOutboxItem(
		PushOutboxItem Item,
		List<PushDelivery> Deliveries,
		DateTime ClaimedUtc);
	#endregion
}
