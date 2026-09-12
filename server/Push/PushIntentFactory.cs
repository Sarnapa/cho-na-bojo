using System.Globalization;
using ChoNaBojo.Contracts.Enums;
using ChoNaBojo.Server.Data.Entities;

namespace ChoNaBojo.Server.Push;

public static class PushIntentFactory
{
	public static PushIntentDescriptor Create(
		EventJoinRequest joinRequest,
		SportsEvent sportsEvent,
		Guid actorUserId,
		EventJoinRequestStatus effectiveTargetStatus,
		bool wasNewlyCreated)
	{
		ArgumentNullException.ThrowIfNull(joinRequest);
		ArgumentNullException.ThrowIfNull(sportsEvent);

		if (!Enum.IsDefined(joinRequest.Status))
		{
			throw new ArgumentOutOfRangeException(
				nameof(joinRequest),
				joinRequest.Status,
				"The join request has an undefined status.");
		}

		if (!Enum.IsDefined(effectiveTargetStatus))
		{
			throw new ArgumentOutOfRangeException(
				nameof(effectiveTargetStatus),
				effectiveTargetStatus,
				"The transition target status is undefined.");
		}

		if (joinRequest.SportsEventId != sportsEvent.Id
			|| joinRequest.Status != effectiveTargetStatus)
		{
			throw new ArgumentException(
				"The join request does not match the completed event transition.",
				nameof(joinRequest));
		}

		Guid recipientUserId;
		PushNotificationType type;
		if (wasNewlyCreated)
		{
			if (joinRequest.RequesterUserId != actorUserId
				|| effectiveTargetStatus is not (
					EventJoinRequestStatus.Pending
					or EventJoinRequestStatus.Accepted))
			{
				throw new ArgumentException(
					"The new join request does not match the requesting actor or target status.",
					nameof(actorUserId));
			}

			recipientUserId = sportsEvent.OrganizerUserId;
			type = PushNotificationType.JoinRequestCreated;
		}
		else
		{
			if (sportsEvent.OrganizerUserId != actorUserId)
			{
				throw new ArgumentException(
					"The join-request resolution actor is not the event organizer.",
					nameof(actorUserId));
			}

			recipientUserId = joinRequest.RequesterUserId;
			type = effectiveTargetStatus switch
			{
				EventJoinRequestStatus.Accepted =>
					PushNotificationType.JoinRequestAccepted,
				EventJoinRequestStatus.Rejected =>
					PushNotificationType.JoinRequestRejected,
				_ => throw new ArgumentOutOfRangeException(
					nameof(effectiveTargetStatus),
					effectiveTargetStatus,
					"Only accepted or rejected requests can be manually resolved.")
			};
		}

		if (!Enum.IsDefined(type))
		{
			throw new InvalidOperationException(
				$"The generated push notification type '{type}' is undefined.");
		}

		return new PushIntentDescriptor(
			recipientUserId,
			type,
			BuildEventKey(joinRequest, type, recipientUserId),
			Guid.NewGuid());
	}

	public static IReadOnlyList<PushIntentDescriptor> CreateLifecycleIntents(
		SportsEvent sportsEvent,
		IReadOnlyCollection<EventJoinRequest> affectedJoinRequests,
		Guid actorUserId)
	{
		ArgumentNullException.ThrowIfNull(sportsEvent);
		ArgumentNullException.ThrowIfNull(affectedJoinRequests);

		if (!Enum.IsDefined(sportsEvent.Status))
		{
			throw new ArgumentOutOfRangeException(
				nameof(sportsEvent),
				sportsEvent.Status,
				"The sports event has an undefined status.");
		}

		if (actorUserId == Guid.Empty)
		{
			throw new ArgumentException(
				"The lifecycle actor cannot be empty.",
				nameof(actorUserId));
		}

		List<EventJoinRequest> joinRequests = affectedJoinRequests.ToList();
		foreach (EventJoinRequest joinRequest in joinRequests)
		{
			if (joinRequest is null)
			{
				throw new ArgumentException(
					"The affected join requests cannot contain null values.",
					nameof(affectedJoinRequests));
			}

			if (!Enum.IsDefined(joinRequest.Status))
			{
				throw new ArgumentOutOfRangeException(
					nameof(affectedJoinRequests),
					joinRequest.Status,
					"An affected join request has an undefined status.");
			}

			if (joinRequest.SportsEventId != sportsEvent.Id)
			{
				throw new ArgumentException(
					"An affected join request does not belong to the sports event.",
					nameof(affectedJoinRequests));
			}
		}

		PushNotificationType type;
		Func<EventJoinRequest, Guid> recipientSelector;
		if (sportsEvent.Status == EventStatus.Cancelled)
		{
			if (sportsEvent.OrganizerUserId != actorUserId
				|| joinRequests.Any(joinRequest =>
					joinRequest.Status != EventJoinRequestStatus.Cancelled))
			{
				throw new ArgumentException(
					"The event cancellation does not match the organizer or affected request states.",
					nameof(actorUserId));
			}

			type = PushNotificationType.EventCancelled;
			recipientSelector = joinRequest => joinRequest.RequesterUserId;
		}
		else
		{
			if (sportsEvent.Status != EventStatus.Active
				|| joinRequests.Count != 1)
			{
				throw new ArgumentException(
					"A participation lifecycle transition requires one request on an active event.",
					nameof(affectedJoinRequests));
			}

			EventJoinRequest joinRequest = joinRequests[0];
			switch (joinRequest.Status)
			{
				case EventJoinRequestStatus.Removed:
					if (sportsEvent.OrganizerUserId != actorUserId)
					{
						throw new ArgumentException(
							"The participant removal actor is not the event organizer.",
							nameof(actorUserId));
					}

					type = PushNotificationType.ParticipantRemoved;
					recipientSelector = request => request.RequesterUserId;
					break;

				case EventJoinRequestStatus.Left:
					if (joinRequest.RequesterUserId != actorUserId)
					{
						throw new ArgumentException(
							"The participant departure actor is not the requester.",
							nameof(actorUserId));
					}

					type = PushNotificationType.ParticipantLeft;
					recipientSelector = _ => sportsEvent.OrganizerUserId;
					break;

				default:
					throw new ArgumentOutOfRangeException(
						nameof(affectedJoinRequests),
						joinRequest.Status,
						"Only cancelled, removed, or left requests produce lifecycle notifications.");
			}
		}

		if (!Enum.IsDefined(type))
		{
			throw new InvalidOperationException(
				$"The generated push notification type '{type}' is undefined.");
		}

		return joinRequests
			.Select(joinRequest =>
			{
				Guid recipientUserId = recipientSelector(joinRequest);
				return new PushIntentDescriptor(
					recipientUserId,
					type,
					BuildEventKey(joinRequest, type, recipientUserId),
					Guid.NewGuid());
			})
			.ToList();
	}

	/// <summary>
	/// Builds the outbox de-duplication key for a notification about one join-request attempt.
	/// </summary>
	/// <remarks>
	/// <see cref="EventJoinRequest.CreatedUtc"/> identifies the <em>current attempt</em>, not the
	/// first-ever creation: a row revived after its requester left replays the whole lifecycle and
	/// can therefore emit a second created / accepted / rejected / left notification for the same
	/// request id. Without the attempt stamp those keys collide with the previous attempt's and the
	/// unique index on <c>PushOutbox.EventKey</c> rejects the write.
	/// </remarks>
	private static string BuildEventKey(
		EventJoinRequest joinRequest,
		PushNotificationType type,
		Guid recipientUserId)
	{
		if (joinRequest.CreatedUtc == default)
		{
			throw new ArgumentException(
				"The join request must carry its current attempt timestamp.",
				nameof(joinRequest));
		}

		return string.Create(
			CultureInfo.InvariantCulture,
			$"join-request:{joinRequest.Id}:{(int)type}:{recipientUserId}:{joinRequest.CreatedUtc.Ticks}");
	}
}

public sealed record PushIntentDescriptor(
	Guid RecipientUserId,
	PushNotificationType Type,
	string EventKey,
	Guid NotificationId);
