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
			$"join-request:{joinRequest.Id}:{(int)type}:{recipientUserId}",
			Guid.NewGuid());
	}
}

public sealed record PushIntentDescriptor(
	Guid RecipientUserId,
	PushNotificationType Type,
	string EventKey,
	Guid NotificationId);
