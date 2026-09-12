using ChoNaBojo.Contracts.Consts;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Contracts.Enums;
using ChoNaBojo.Server.Auth;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.Data.Entities;
using ChoNaBojo.Server.Push;
using ChoNaBojo.Utils.Text;
using ChoNaBojo.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ChoNaBojo.Server.Events;

public static class EventEndpoints
{
	#region Private constants
	private const string UniqueRequestConstraint =
		"IX_SportsEvents_OrganizerUserId_ClientRequestId";
	private const string VenueSportForeignKeyConstraint =
		"FK_SportsEvents_VenueSports_VenueId_SportId";
	private const string UniqueJoinRequestConstraint =
		"IX_EventJoinRequests_SportsEventId_RequesterUserId";
	private const string JoinRequestEventForeignKeyConstraint =
		"FK_EventJoinRequests_SportsEvents_SportsEventId";
	#endregion

	#region Public methods
	public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/events", CreateEventAsync)
			.WithName("EventsCreate");

		endpoints.MapGet("/venues/{venueId:int}/events", GetVenueEventsAsync)
			.WithName("VenueEventsList");

		endpoints.MapPost("/events/{eventId:guid}/join-requests", RequestToJoinEventAsync)
			.WithName("EventsJoinRequestsCreate");

		endpoints.MapPost(
				"/events/{eventId:guid}/join-requests/{requestId:guid}/accept",
				AcceptJoinRequestAsync)
			.WithName("EventsJoinRequestsAccept");

		endpoints.MapPost(
				"/events/{eventId:guid}/join-requests/{requestId:guid}/reject",
				RejectJoinRequestAsync)
			.WithName("EventsJoinRequestsReject");

		endpoints.MapPost("/events/{eventId:guid}/cancel", CancelEventAsync)
			.WithName("EventsCancel");

		endpoints.MapGet("/me/events", GetMyEventsAsync)
			.WithName("MeEventsList");

		endpoints.MapGet(
				"/events/{eventId:guid}/join-requests",
				GetEventJoinRequestsAsync)
			.WithName("EventsJoinRequestsList");

		endpoints.MapGet("/events/{eventId:guid}/contacts", GetEventContactsAsync)
			.WithName("EventContactsList");

		return endpoints;
	}
	#endregion

	#region Private methods
	private static async Task<IResult> CreateEventAsync(
		CreateEventRequest request,
		ChoNaBojoContext dbContext,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		Guid organizerUserId = httpContext.GetUserId();
		SportsEvent? existingEvent = await FindEventAsync(
			dbContext,
			organizerUserId,
			request.ClientRequestId,
			cancellationToken);

		if (existingEvent is not null)
		{
			return Results.Ok(ToResponse(existingEvent));
		}

		// Validate the same values that will be persisted: truncating after validation would let a
		// sub-microsecond duration pass the validator and then violate CK_SportsEvents_TimeRange.
		CreateEventRequest normalizedRequest = request with
		{
			StartsAtUtc = TruncateToMicrosecond(request.StartsAtUtc),
			EstimatedEndsAtUtc = TruncateToMicrosecond(request.EstimatedEndsAtUtc)
		};

		ValidationResult validation = EventValidation.ValidateCreateEventRequest(
			normalizedRequest,
			DateTimeOffset.UtcNow);
		if (!validation.IsValid)
		{
			return ToValidationProblem(validation);
		}

		VenueSport? venueSport = await dbContext.VenueSports
			.Include(entity => entity.Venue)
			.Include(entity => entity.Sport)
			.SingleOrDefaultAsync(
				entity => entity.VenueId == request.VenueId
					&& entity.SportId == request.SportId,
				cancellationToken);

		if (venueSport is null)
		{
			return await ClassifyMissingReferenceAsync(
				dbContext,
				request.VenueId,
				request.SportId,
				cancellationToken);
		}

		var sportsEvent = new SportsEvent
		{
			OrganizerUserId = organizerUserId,
			ClientRequestId = request.ClientRequestId,
			VenueId = request.VenueId,
			SportId = request.SportId,
			Title = request.Title.Trim(),
			Description = TextNormalization.NormalizeOptionalText(request.Description),
			StartsAtUtc = NormalizeUtcTimestamp(normalizedRequest.StartsAtUtc),
			EstimatedEndsAtUtc = NormalizeUtcTimestamp(normalizedRequest.EstimatedEndsAtUtc),
			CreatedUtc = NormalizeUtcTimestamp(DateTimeOffset.UtcNow),
			ParticipantLimit = request.ParticipantLimit,
			AutoAccept = request.AutoAccept,
			VenueSport = venueSport
		};

		dbContext.SportsEvents.Add(sportsEvent);

		try
		{
			await dbContext.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException exception) when (HasConstraint(exception, UniqueRequestConstraint))
		{
			dbContext.Entry(sportsEvent).State = EntityState.Detached;
			existingEvent = await FindEventAsync(
				dbContext,
				organizerUserId,
				request.ClientRequestId,
				cancellationToken);

			if (existingEvent is null)
			{
				throw;
			}

			return Results.Ok(ToResponse(existingEvent));
		}
		catch (DbUpdateException exception) when (HasConstraint(exception, VenueSportForeignKeyConstraint))
		{
			dbContext.Entry(sportsEvent).State = EntityState.Detached;
			bool venueStillExists = await dbContext.Venues
				.AsNoTracking()
				.AnyAsync(entity => entity.Id == request.VenueId, cancellationToken);
			string field = venueStillExists ? "sportId" : "venueId";

			return Results.Conflict(new EventConflictResponse(
				"reference_data_changed",
				field,
				"The venue or its supported sports changed. Refresh the venue catalog and try again."));
		}
		catch (DbUpdateException)
		{
			// Terminal guard: no database constraint may surface as a 500, which the client maps to
			// its unknown branch and offers a Retry that resends the identical, always-failing snapshot.
			dbContext.Entry(sportsEvent).State = EntityState.Detached;

			return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
			{
				["event"] =
				[
					"The event details violate a stored data rule and could not be saved. Adjust the event and try again."
				]
			});
		}

		return Results.Json(
			ToResponse(sportsEvent),
			statusCode: StatusCodes.Status201Created);
	}

	private static async Task<IResult> GetVenueEventsAsync(
		int venueId,
		[AsParameters] EventListingQuery query,
		ChoNaBojoContext dbContext,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		ValidationResult validation = EventListingValidation.ValidateEventListingQuery(query);
		if (!validation.IsValid)
		{
			return ToValidationProblem(validation);
		}

		IResult? referenceConflict = await ClassifyVenueSportReferenceAsync(
			dbContext,
			venueId,
			query.SportId,
			cancellationToken);
		if (referenceConflict is not null)
		{
			return referenceConflict;
		}

		Guid callerUserId = httpContext.GetUserId();
		// One captured instant so list inclusion and later join joinability never drift mid-request.
		DateTime nowUtc = NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
		DateTime? availableFromUtc = query.AvailableFromUtc.HasValue
			? NormalizeUtcTimestamp(query.AvailableFromUtc.Value)
			: null;
		DateTime? availableToUtc = query.AvailableToUtc.HasValue
			? NormalizeUtcTimestamp(query.AvailableToUtc.Value)
			: null;

		var rows = await dbContext.SportsEvents
			.AsNoTracking()
			.Where(sportsEvent => sportsEvent.VenueId == venueId)
			.Where(sportsEvent => sportsEvent.Status == EventStatus.Active)
			.Where(sportsEvent => sportsEvent.EstimatedEndsAtUtc > nowUtc)
			.Where(sportsEvent => query.SportId == null || sportsEvent.SportId == query.SportId)
			.Where(sportsEvent => availableFromUtc == null || availableToUtc == null
				|| (sportsEvent.StartsAtUtc < availableToUtc.Value
					&& sportsEvent.EstimatedEndsAtUtc > availableFromUtc.Value))
			.OrderBy(sportsEvent => sportsEvent.StartsAtUtc)
			.ThenBy(sportsEvent => sportsEvent.Id)
			.Select(sportsEvent => new
			{
				sportsEvent.Id,
				sportsEvent.Title,
				sportsEvent.Description,
				sportsEvent.StartsAtUtc,
				sportsEvent.EstimatedEndsAtUtc,
				sportsEvent.ParticipantLimit,
				sportsEvent.AutoAccept,
				sportsEvent.SportId,
				SportCode = sportsEvent.VenueSport.Sport.Code,
				SportName = sportsEvent.VenueSport.Sport.Name,
				IsOrganizer = sportsEvent.OrganizerUserId == callerUserId,
				AcceptedCount = sportsEvent.EventJoinRequests
					.Count(request => request.Status == EventJoinRequestStatus.Accepted),
				CurrentUserRequestStatus = sportsEvent.EventJoinRequests
					.Where(request => request.RequesterUserId == callerUserId)
					.Select(request => (EventJoinRequestStatus?)request.Status)
					.FirstOrDefault()
			})
			.ToListAsync(cancellationToken);

		var events = rows.Select(row => new EventListItemResponse(
			row.Id,
			row.Title,
			row.Description,
			new DateTimeOffset(row.StartsAtUtc, TimeSpan.Zero),
			new DateTimeOffset(row.EstimatedEndsAtUtc, TimeSpan.Zero),
			row.ParticipantLimit,
			1 + row.AcceptedCount,
			row.AutoAccept,
			new EventSportSummary(row.SportId, row.SportCode, row.SportName),
			row.IsOrganizer,
			row.CurrentUserRequestStatus));

		return Results.Ok(events);
	}

	private static async Task<IResult?> ClassifyVenueSportReferenceAsync(
		ChoNaBojoContext dbContext,
		int venueId,
		int? sportId,
		CancellationToken cancellationToken)
	{
		bool venueExists = await dbContext.Venues
			.AsNoTracking()
			.AnyAsync(entity => entity.Id == venueId, cancellationToken);
		if (!venueExists)
		{
			return Results.Conflict(new EventConflictResponse(
				EventConflictCodes.VenueNotFound,
				"venueId",
				"The selected venue no longer exists."));
		}

		if (sportId.HasValue)
		{
			bool venueSportExists = await dbContext.VenueSports
				.AsNoTracking()
				.AnyAsync(
					entity => entity.VenueId == venueId && entity.SportId == sportId.Value,
					cancellationToken);
			if (!venueSportExists)
			{
				return Results.Conflict(new EventConflictResponse(
					EventConflictCodes.SportNotSupportedAtVenue,
					"sportId",
					"The selected sport is not supported at this venue."));
			}
		}

		return null;
	}

	private static async Task<IResult> RequestToJoinEventAsync(
		Guid eventId,
		ChoNaBojoContext dbContext,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		Guid requesterUserId = httpContext.GetUserId();

		// Look up any existing request before applying dynamic expiry/capacity/organizer checks so an
		// exact retry replays the stored outcome even if the event ended or filled meanwhile.
		EventJoinRequest? existingRequest = await FindJoinRequestAsync(
			dbContext,
			eventId,
			requesterUserId,
			cancellationToken);
		if (existingRequest is not null)
		{
			return Results.Ok(ToJoinResponse(existingRequest));
		}

		JoinRequestTransitionResult transition = await TransitionJoinRequestAsync(
			dbContext,
			eventId,
			requesterUserId,
			requestId: null,
			EventJoinRequestStatus.Accepted,
			createIfMissing: true,
			cancellationToken);

		return ToJoinCreationResult(transition);
	}

	private static async Task<IResult> AcceptJoinRequestAsync(
		Guid eventId,
		Guid requestId,
		ChoNaBojoContext dbContext,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		return await ResolveJoinRequestAsync(
			dbContext,
			eventId,
			requestId,
			httpContext.GetUserId(),
			EventJoinRequestStatus.Accepted,
			cancellationToken);
	}

	private static async Task<IResult> RejectJoinRequestAsync(
		Guid eventId,
		Guid requestId,
		ChoNaBojoContext dbContext,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		return await ResolveJoinRequestAsync(
			dbContext,
			eventId,
			requestId,
			httpContext.GetUserId(),
			EventJoinRequestStatus.Rejected,
			cancellationToken);
	}

	private static async Task<IResult> CancelEventAsync(
		Guid eventId,
		ChoNaBojoContext dbContext,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		Guid organizerUserId = httpContext.GetUserId();
		await using var transaction = await dbContext.Database.BeginTransactionAsync(
			cancellationToken);

		await dbContext.Database.ExecuteSqlAsync(
			$"""SELECT 1 FROM "SportsEvents" WHERE "Id" = {eventId} FOR UPDATE""",
			cancellationToken);

		SportsEvent? sportsEvent = await dbContext.SportsEvents
			.SingleOrDefaultAsync(
				entity => entity.Id == eventId
					&& entity.OrganizerUserId == organizerUserId,
				cancellationToken);
		if (sportsEvent is null)
		{
			await transaction.RollbackAsync(cancellationToken);
			return Results.NotFound(new EventConflictResponse(
				EventConflictCodes.EventNotFound,
				"eventId",
				"The event is no longer available."));
		}

		if (sportsEvent.Status == EventStatus.Cancelled)
		{
			await transaction.CommitAsync(cancellationToken);
			return Results.Ok(ToCancelResponse(sportsEvent, notifiedParticipantCount: 0));
		}

		DateTime cancelledUtc = NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
		if (sportsEvent.Status == EventStatus.Closed
			|| sportsEvent.EstimatedEndsAtUtc <= cancelledUtc)
		{
			await transaction.RollbackAsync(cancellationToken);
			return Results.Conflict(new EventConflictResponse(
				EventConflictCodes.EventEnded,
				"eventId",
				"This event has already ended."));
		}

		List<EventJoinRequest> affectedJoinRequests = await dbContext.EventJoinRequests
			.Where(request => request.SportsEventId == eventId
				&& (request.Status == EventJoinRequestStatus.Pending
					|| request.Status == EventJoinRequestStatus.Accepted))
			.ToListAsync(cancellationToken);

		foreach (EventJoinRequest joinRequest in affectedJoinRequests)
		{
			joinRequest.Status = EventJoinRequestStatus.Cancelled;
			joinRequest.UpdatedUtc = cancelledUtc;
		}

		sportsEvent.Status = EventStatus.Cancelled;
		sportsEvent.StatusChangedUtc = cancelledUtc;

		IReadOnlyList<PushIntentDescriptor> intents =
			PushIntentFactory.CreateLifecycleIntents(
				sportsEvent,
				affectedJoinRequests,
				organizerUserId);
		Dictionary<Guid, Guid> requestIdsByRecipient = affectedJoinRequests
			.ToDictionary(
				request => request.RequesterUserId,
				request => request.Id);
		dbContext.PushOutbox.AddRange(intents.Select(intent => new PushOutboxItem
		{
			EventKey = intent.EventKey,
			RecipientUserId = intent.RecipientUserId,
			Type = intent.Type,
			SportsEventId = sportsEvent.Id,
			EventJoinRequestId = requestIdsByRecipient[intent.RecipientUserId],
			NotificationId = intent.NotificationId,
			OccurredUtc = cancelledUtc,
			NextAttemptUtc = cancelledUtc
		}));

		await dbContext.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);

		return Results.Ok(ToCancelResponse(
			sportsEvent,
			affectedJoinRequests.Count));
	}

	private static async Task<IResult> GetMyEventsAsync(
		ChoNaBojoContext dbContext,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		Guid callerUserId = httpContext.GetUserId();

		var organizedRows = await dbContext.SportsEvents
			.AsNoTracking()
			.Where(sportsEvent => sportsEvent.OrganizerUserId == callerUserId)
			.OrderBy(sportsEvent => sportsEvent.StartsAtUtc)
			.ThenBy(sportsEvent => sportsEvent.Id)
			.Select(sportsEvent => new
			{
				sportsEvent.Id,
				sportsEvent.Title,
				sportsEvent.StartsAtUtc,
				sportsEvent.EstimatedEndsAtUtc,
				sportsEvent.ParticipantLimit,
				sportsEvent.AutoAccept,
				sportsEvent.VenueId,
				VenueName = sportsEvent.VenueSport.Venue.Name,
				VenueAddress = sportsEvent.VenueSport.Venue.Address,
				sportsEvent.SportId,
				sportsEvent.Status,
				SportCode = sportsEvent.VenueSport.Sport.Code,
				SportName = sportsEvent.VenueSport.Sport.Name,
				AcceptedCount = sportsEvent.EventJoinRequests
					.Count(request => request.Status == EventJoinRequestStatus.Accepted),
				PendingRequestCount = sportsEvent.EventJoinRequests
					.Count(request => request.Status == EventJoinRequestStatus.Pending)
			})
			.ToListAsync(cancellationToken);

		var requestedRows = await dbContext.EventJoinRequests
			.AsNoTracking()
			.Where(request => request.RequesterUserId == callerUserId)
			.OrderBy(request => request.SportsEvent.StartsAtUtc)
			.ThenBy(request => request.SportsEventId)
			.Select(request => new
			{
				request.SportsEventId,
				RequestId = request.Id,
				request.SportsEvent.Title,
				request.SportsEvent.StartsAtUtc,
				request.SportsEvent.EstimatedEndsAtUtc,
				request.SportsEvent.ParticipantLimit,
				request.SportsEvent.AutoAccept,
				request.SportsEvent.VenueId,
				VenueName = request.SportsEvent.VenueSport.Venue.Name,
				VenueAddress = request.SportsEvent.VenueSport.Venue.Address,
				request.SportsEvent.SportId,
				EventStatus = request.SportsEvent.Status,
				SportCode = request.SportsEvent.VenueSport.Sport.Code,
				SportName = request.SportsEvent.VenueSport.Sport.Name,
				request.Status,
				request.UpdatedUtc,
				AcceptedCount = request.SportsEvent.EventJoinRequests
					.Count(joinRequest =>
						joinRequest.Status == EventJoinRequestStatus.Accepted)
			})
			.ToListAsync(cancellationToken);

		var organizedEvents = organizedRows
			.Select(row => new OrganizedEventResponse(
				row.Id,
				row.Title,
				new DateTimeOffset(row.StartsAtUtc, TimeSpan.Zero),
				new DateTimeOffset(row.EstimatedEndsAtUtc, TimeSpan.Zero),
				row.ParticipantLimit,
				1 + row.AcceptedCount,
				row.AutoAccept,
				new EventVenueSummary(row.VenueId, row.VenueName, row.VenueAddress),
				new EventSportSummary(row.SportId, row.SportCode, row.SportName),
				row.PendingRequestCount,
				row.Status))
			.ToList();

		var requestedEvents = requestedRows
			.Select(row => new RequestedEventResponse(
				row.SportsEventId,
				row.RequestId,
				row.Title,
				new DateTimeOffset(row.StartsAtUtc, TimeSpan.Zero),
				new DateTimeOffset(row.EstimatedEndsAtUtc, TimeSpan.Zero),
				row.ParticipantLimit,
				1 + row.AcceptedCount,
				row.AutoAccept,
				new EventVenueSummary(row.VenueId, row.VenueName, row.VenueAddress),
				new EventSportSummary(row.SportId, row.SportCode, row.SportName),
				row.Status,
				row.UpdatedUtc.HasValue
					? new DateTimeOffset(row.UpdatedUtc.Value, TimeSpan.Zero)
					: null,
				row.EventStatus))
			.ToList();

		return Results.Ok(new MyEventsResponse(organizedEvents, requestedEvents));
	}

	private static async Task<IResult> GetEventJoinRequestsAsync(
		Guid eventId,
		ChoNaBojoContext dbContext,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		Guid callerUserId = httpContext.GetUserId();
		var eventQueue = await dbContext.SportsEvents
			.AsNoTracking()
			.Where(sportsEvent => sportsEvent.Id == eventId
				&& sportsEvent.OrganizerUserId == callerUserId)
			.Select(sportsEvent => new
			{
				Requests = sportsEvent.EventJoinRequests
					.OrderBy(request => request.CreatedUtc)
					.ThenBy(request => request.Id)
					.Select(request => new
					{
						request.Id,
						request.Status,
						request.CreatedUtc,
						request.UpdatedUtc
					})
					.ToList()
			})
			.SingleOrDefaultAsync(cancellationToken);

		if (eventQueue is null)
		{
			return RequestNotFound();
		}

		var queue = eventQueue.Requests
			.Select((request, index) => new EventJoinRequestQueueItemResponse(
				request.Id,
				$"Requester {index + 1}",
				request.Status,
				new DateTimeOffset(request.CreatedUtc, TimeSpan.Zero),
				request.UpdatedUtc.HasValue
					? new DateTimeOffset(request.UpdatedUtc.Value, TimeSpan.Zero)
					: null))
			.OrderBy(request => request.Status == EventJoinRequestStatus.Pending ? 0 : 1)
			.ThenBy(request => request.CreatedUtc)
			.ThenBy(request => request.RequestId)
			.ToList();

		return Results.Ok(queue);
	}

	// This is the only endpoint permitted to read shareable User contact columns.
	private static async Task<IResult> GetEventContactsAsync(
		Guid eventId,
		ChoNaBojoContext dbContext,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		Guid callerUserId = httpContext.GetUserId();
		var entitlement = await dbContext.SportsEvents
			.AsNoTracking()
			.Where(sportsEvent => sportsEvent.Id == eventId
				&& sportsEvent.Status == EventStatus.Active)
			.Select(sportsEvent => new
			{
				IsOrganizer = sportsEvent.OrganizerUserId == callerUserId,
				AcceptedRequestId = sportsEvent.EventJoinRequests
					.Where(request => request.RequesterUserId == callerUserId
						&& request.Status == EventJoinRequestStatus.Accepted)
					.Select(request => (Guid?)request.Id)
					.SingleOrDefault(),
				Organizer = new
				{
					sportsEvent.Organizer.Id,
					sportsEvent.Organizer.ContactPhone,
					sportsEvent.Organizer.ContactEmail,
					sportsEvent.Organizer.CommunicatorPlatform,
					sportsEvent.Organizer.CommunicatorHandle
				},
				AcceptedParticipants = sportsEvent.EventJoinRequests
					.Where(request => sportsEvent.OrganizerUserId == callerUserId
						&& request.Status == EventJoinRequestStatus.Accepted)
					.OrderBy(request => request.CreatedUtc)
					.ThenBy(request => request.Id)
					.Select(request => new
					{
						RequestId = request.Id,
						request.Requester.Id,
						request.Requester.ContactPhone,
						request.Requester.ContactEmail,
						request.Requester.CommunicatorPlatform,
						request.Requester.CommunicatorHandle
					})
					.ToList()
			})
			.SingleOrDefaultAsync(cancellationToken);

		if (entitlement is null
			|| (!entitlement.IsOrganizer && !entitlement.AcceptedRequestId.HasValue))
		{
			return RequestNotFound();
		}

		IReadOnlyList<EventContactResponse> contacts = entitlement.IsOrganizer
			? entitlement.AcceptedParticipants
				.Select(participant => new EventContactResponse(
					participant.Id,
					participant.RequestId,
					IsOrganizer: false,
					new ContactInfoResponse(
						participant.ContactPhone,
						participant.ContactEmail,
						participant.CommunicatorPlatform,
						participant.CommunicatorHandle)))
				.ToList()
			:
			[
				new EventContactResponse(
					entitlement.Organizer.Id,
					JoinRequestId: null,
					IsOrganizer: true,
					new ContactInfoResponse(
						entitlement.Organizer.ContactPhone,
						entitlement.Organizer.ContactEmail,
						entitlement.Organizer.CommunicatorPlatform,
						entitlement.Organizer.CommunicatorHandle))
			];

		return Results.Ok(new EventContactsResponse(contacts));
	}

	private static async Task<IResult> ResolveJoinRequestAsync(
		ChoNaBojoContext dbContext,
		Guid eventId,
		Guid requestId,
		Guid organizerUserId,
		EventJoinRequestStatus targetStatus,
		CancellationToken cancellationToken)
	{
		JoinRequestTransitionResult transition = await TransitionJoinRequestAsync(
			dbContext,
			eventId,
			organizerUserId,
			requestId,
			targetStatus,
			createIfMissing: false,
			cancellationToken);

		if (transition.Request is not null)
		{
			return Results.Ok(ToJoinResponse(transition.Request));
		}

		return transition.Failure switch
		{
			JoinRequestTransitionFailure.RequestNotFound =>
				Results.NotFound(new EventConflictResponse(
					EventConflictCodes.RequestNotFound,
					"requestId",
					"The join request is no longer available.")),
			JoinRequestTransitionFailure.RequestAlreadyResolved =>
				Results.Conflict(new EventConflictResponse(
					EventConflictCodes.RequestAlreadyResolved,
					"requestId",
					"The join request has already been resolved.")),
			JoinRequestTransitionFailure.EventEnded =>
				Results.Conflict(new EventConflictResponse(
					EventConflictCodes.EventEnded,
					"eventId",
					"This event has already ended.")),
			JoinRequestTransitionFailure.EventCancelled =>
				Results.Conflict(new EventConflictResponse(
					EventConflictCodes.EventCancelled,
					"eventId",
					"This event has been cancelled.")),
			JoinRequestTransitionFailure.EventFull =>
				Results.Conflict(new EventConflictResponse(
					EventConflictCodes.EventFull,
					"eventId",
					"This event has reached its participant limit.")),
			_ => throw new InvalidOperationException(
				$"Unexpected join-request transition failure: {transition.Failure}.")
		};
	}

	private static async Task<JoinRequestTransitionResult> TransitionJoinRequestAsync(
		ChoNaBojoContext dbContext,
		Guid eventId,
		Guid actorUserId,
		Guid? requestId,
		EventJoinRequestStatus targetStatus,
		bool createIfMissing,
		CancellationToken cancellationToken)
	{
		await using var transaction = await dbContext.Database.BeginTransactionAsync(
			cancellationToken);
		EventJoinRequest? joinRequest = null;

		try
		{
			await dbContext.Database.ExecuteSqlAsync(
				$"""SELECT 1 FROM "SportsEvents" WHERE "Id" = {eventId} FOR UPDATE""",
				cancellationToken);

			SportsEvent? sportsEvent;
			if (createIfMissing)
			{
				sportsEvent = await dbContext.SportsEvents
					.SingleOrDefaultAsync(
						entity => entity.Id == eventId,
						cancellationToken);
				if (sportsEvent is null)
				{
					await transaction.RollbackAsync(cancellationToken);
					return JoinRequestTransitionResult.Failed(
						JoinRequestTransitionFailure.EventNotFound);
				}

				joinRequest = await dbContext.EventJoinRequests
					.SingleOrDefaultAsync(
						request => request.SportsEventId == eventId
							&& request.RequesterUserId == actorUserId,
						cancellationToken);
				if (joinRequest is not null)
				{
					await transaction.CommitAsync(cancellationToken);
					return JoinRequestTransitionResult.Succeeded(
						joinRequest,
						wasCreated: false);
				}

				if (sportsEvent.OrganizerUserId == actorUserId)
				{
					await transaction.RollbackAsync(cancellationToken);
					return JoinRequestTransitionResult.Failed(
						JoinRequestTransitionFailure.OrganizerCannotJoin);
				}

				DateTime createdUtc = NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
				joinRequest = new EventJoinRequest
				{
					SportsEventId = eventId,
					RequesterUserId = actorUserId,
					Status = EventJoinRequestStatus.Pending,
					CreatedUtc = createdUtc
				};
				dbContext.EventJoinRequests.Add(joinRequest);
			}
			else
			{
				Guid existingRequestId = requestId
					?? throw new InvalidOperationException(
						"An existing request id is required for manual resolution.");
				joinRequest = await dbContext.EventJoinRequests
					.Include(request => request.SportsEvent)
					.SingleOrDefaultAsync(
						request => request.Id == existingRequestId
							&& request.SportsEventId == eventId
							&& request.SportsEvent.OrganizerUserId == actorUserId,
						cancellationToken);
				if (joinRequest is null)
				{
					await transaction.RollbackAsync(cancellationToken);
					return JoinRequestTransitionResult.Failed(
						JoinRequestTransitionFailure.RequestNotFound);
				}

				sportsEvent = joinRequest.SportsEvent;
				if (joinRequest.Status == targetStatus)
				{
					await transaction.CommitAsync(cancellationToken);
					return JoinRequestTransitionResult.Succeeded(
						joinRequest,
						wasCreated: false);
				}

				if (joinRequest.Status != EventJoinRequestStatus.Pending)
				{
					await transaction.RollbackAsync(cancellationToken);
					return JoinRequestTransitionResult.Failed(
						JoinRequestTransitionFailure.RequestAlreadyResolved);
				}
			}

			DateTime resolvedUtc = NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
			EventJoinRequestStatus effectiveTargetStatus =
				createIfMissing && !sportsEvent.AutoAccept
					? EventJoinRequestStatus.Pending
					: targetStatus;
			if (effectiveTargetStatus == EventJoinRequestStatus.Accepted)
			{
				JoinRequestTransitionFailure? failure =
					await ClaimParticipantSlotUnderLockAsync(
						dbContext,
						sportsEvent,
						joinRequest,
						resolvedUtc,
						cancellationToken);
				if (failure.HasValue)
				{
					await transaction.RollbackAsync(cancellationToken);
					if (createIfMissing)
					{
						dbContext.Entry(joinRequest).State = EntityState.Detached;
					}

					return JoinRequestTransitionResult.Failed(failure.Value);
				}
			}
			else if (effectiveTargetStatus == EventJoinRequestStatus.Rejected)
			{
				joinRequest.Status = EventJoinRequestStatus.Rejected;
				joinRequest.UpdatedUtc = resolvedUtc;
			}
			else if (effectiveTargetStatus == EventJoinRequestStatus.Pending
				&& createIfMissing)
			{
				JoinRequestTransitionFailure? failure =
					await GetJoinabilityFailureUnderLockAsync(
						dbContext,
						sportsEvent,
						resolvedUtc,
						cancellationToken);
				if (failure.HasValue)
				{
					await transaction.RollbackAsync(cancellationToken);
					dbContext.Entry(joinRequest).State = EntityState.Detached;
					return JoinRequestTransitionResult.Failed(failure.Value);
				}
			}
			else
			{
				throw new InvalidOperationException(
					$"Unsupported join-request target status: {effectiveTargetStatus}.");
			}

			await dbContext.SaveChangesAsync(cancellationToken);

			PushIntentDescriptor intent = PushIntentFactory.Create(
				joinRequest,
				sportsEvent,
				actorUserId,
				effectiveTargetStatus,
				wasNewlyCreated: createIfMissing);
			dbContext.PushOutbox.Add(new PushOutboxItem
			{
				EventKey = intent.EventKey,
				RecipientUserId = intent.RecipientUserId,
				Type = intent.Type,
				SportsEventId = sportsEvent.Id,
				EventJoinRequestId = joinRequest.Id,
				NotificationId = intent.NotificationId,
				OccurredUtc = resolvedUtc,
				NextAttemptUtc = resolvedUtc
			});
			await dbContext.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);

			return JoinRequestTransitionResult.Succeeded(
				joinRequest,
				wasCreated: createIfMissing);
		}
		catch (DbUpdateException exception)
			when (createIfMissing
				&& HasConstraint(exception, UniqueJoinRequestConstraint))
		{
			await transaction.RollbackAsync(cancellationToken);
			if (joinRequest is not null)
			{
				dbContext.Entry(joinRequest).State = EntityState.Detached;
			}

			EventJoinRequest? existingRequest = await FindJoinRequestAsync(
				dbContext,
				eventId,
				actorUserId,
				cancellationToken);
			if (existingRequest is null)
			{
				throw;
			}

			return JoinRequestTransitionResult.Succeeded(
				existingRequest,
				wasCreated: false);
		}
		catch (DbUpdateException exception)
			when (createIfMissing
				&& HasConstraint(exception, JoinRequestEventForeignKeyConstraint))
		{
			await transaction.RollbackAsync(cancellationToken);
			if (joinRequest is not null)
			{
				dbContext.Entry(joinRequest).State = EntityState.Detached;
			}

			return JoinRequestTransitionResult.Failed(
				JoinRequestTransitionFailure.EventNotFound);
		}
	}

	private static async Task<JoinRequestTransitionFailure?>
		ClaimParticipantSlotUnderLockAsync(
			ChoNaBojoContext dbContext,
			SportsEvent sportsEvent,
			EventJoinRequest joinRequest,
			DateTime resolvedUtc,
			CancellationToken cancellationToken)
	{
		JoinRequestTransitionFailure? failure =
			await GetJoinabilityFailureUnderLockAsync(
				dbContext,
				sportsEvent,
				resolvedUtc,
				cancellationToken);
		if (failure.HasValue)
		{
			return failure;
		}

		joinRequest.Status = EventJoinRequestStatus.Accepted;
		joinRequest.UpdatedUtc = resolvedUtc;
		return null;
	}

	private static async Task<JoinRequestTransitionFailure?>
		GetJoinabilityFailureUnderLockAsync(
			ChoNaBojoContext dbContext,
			SportsEvent sportsEvent,
			DateTime nowUtc,
			CancellationToken cancellationToken)
	{
		if (sportsEvent.Status == EventStatus.Cancelled)
		{
			return JoinRequestTransitionFailure.EventCancelled;
		}

		if (sportsEvent.Status == EventStatus.Closed
			|| sportsEvent.EstimatedEndsAtUtc <= nowUtc)
		{
			return JoinRequestTransitionFailure.EventEnded;
		}

		int acceptedCount = await dbContext.EventJoinRequests
			.CountAsync(
				request => request.SportsEventId == sportsEvent.Id
					&& request.Status == EventJoinRequestStatus.Accepted,
				cancellationToken);
		return 1 + acceptedCount >= sportsEvent.ParticipantLimit
			? JoinRequestTransitionFailure.EventFull
			: null;
	}

	private static IResult ToJoinCreationResult(JoinRequestTransitionResult transition)
	{
		if (transition.Request is not null)
		{
			JoinRequestResponse response = ToJoinResponse(transition.Request);
			return transition.WasCreated
				? Results.Json(response, statusCode: StatusCodes.Status201Created)
				: Results.Ok(response);
		}

		return transition.Failure switch
		{
			JoinRequestTransitionFailure.EventNotFound =>
				Results.Conflict(new EventConflictResponse(
					EventConflictCodes.EventNotFound,
					"eventId",
					"The selected event no longer exists.")),
			JoinRequestTransitionFailure.EventEnded =>
				Results.Conflict(new EventConflictResponse(
					EventConflictCodes.EventEnded,
					"eventId",
					"This event has already ended.")),
			JoinRequestTransitionFailure.EventCancelled =>
				Results.Conflict(new EventConflictResponse(
					EventConflictCodes.EventCancelled,
					"eventId",
					"This event has been cancelled.")),
			JoinRequestTransitionFailure.EventFull =>
				Results.Conflict(new EventConflictResponse(
					EventConflictCodes.EventFull,
					"eventId",
					"This event has reached its participant limit.")),
			JoinRequestTransitionFailure.OrganizerCannotJoin =>
				Results.Conflict(new EventConflictResponse(
					EventConflictCodes.OrganizerCannotJoin,
					"eventId",
					"You cannot join an event you are organizing.")),
			_ => throw new InvalidOperationException(
				$"Unexpected join-request creation failure: {transition.Failure}.")
		};
	}

	private static async Task<EventJoinRequest?> FindJoinRequestAsync(
		ChoNaBojoContext dbContext,
		Guid eventId,
		Guid requesterUserId,
		CancellationToken cancellationToken)
	{
		return await dbContext.EventJoinRequests
			.AsNoTracking()
			.SingleOrDefaultAsync(
				request => request.SportsEventId == eventId
					&& request.RequesterUserId == requesterUserId,
				cancellationToken);
	}

	private static JoinRequestResponse ToJoinResponse(EventJoinRequest joinRequest)
	{
		return new JoinRequestResponse(
			joinRequest.Id,
			joinRequest.SportsEventId,
			joinRequest.Status,
			new DateTimeOffset(joinRequest.CreatedUtc, TimeSpan.Zero),
			joinRequest.UpdatedUtc.HasValue
				? new DateTimeOffset(joinRequest.UpdatedUtc.Value, TimeSpan.Zero)
				: null);
	}

	private static CancelEventResponse ToCancelResponse(
		SportsEvent sportsEvent,
		int notifiedParticipantCount)
	{
		DateTime statusChangedUtc = sportsEvent.StatusChangedUtc
			?? throw new InvalidOperationException(
				"A cancelled event must have a status-change timestamp.");

		return new CancelEventResponse(
			sportsEvent.Id,
			sportsEvent.Status,
			new DateTimeOffset(statusChangedUtc, TimeSpan.Zero),
			notifiedParticipantCount);
	}

	private static IResult RequestNotFound()
	{
		return Results.NotFound(new EventConflictResponse(
			EventConflictCodes.RequestNotFound,
			"eventId",
			"The event or join requests are no longer available."));
	}

	private static async Task<SportsEvent?> FindEventAsync(
		ChoNaBojoContext dbContext,
		Guid organizerUserId,
		Guid clientRequestId,
		CancellationToken cancellationToken)
	{
		return await dbContext.SportsEvents
			.AsNoTracking()
			.Include(entity => entity.VenueSport)
				.ThenInclude(entity => entity.Venue)
			.Include(entity => entity.VenueSport)
				.ThenInclude(entity => entity.Sport)
			.SingleOrDefaultAsync(
				entity => entity.OrganizerUserId == organizerUserId
					&& entity.ClientRequestId == clientRequestId,
				cancellationToken);
	}

	private static async Task<IResult> ClassifyMissingReferenceAsync(
		ChoNaBojoContext dbContext,
		int venueId,
		int sportId,
		CancellationToken cancellationToken)
	{
		bool venueExists = await dbContext.Venues
			.AsNoTracking()
			.AnyAsync(entity => entity.Id == venueId, cancellationToken);
		if (!venueExists)
		{
			return Results.Conflict(new EventConflictResponse(
				EventConflictCodes.VenueNotFound,
				"venueId",
				"The selected venue no longer exists."));
		}

		bool sportExists = await dbContext.Sports
			.AsNoTracking()
			.AnyAsync(entity => entity.Id == sportId, cancellationToken);
		if (!sportExists)
		{
			return Results.Conflict(new EventConflictResponse(
				EventConflictCodes.SportNotFound,
				"sportId",
				"The selected sport no longer exists."));
		}

		return Results.Conflict(new EventConflictResponse(
			EventConflictCodes.SportNotSupportedAtVenue,
			"sportId",
			"The selected sport is not supported at this venue."));
	}

	private static DateTime NormalizeUtcTimestamp(DateTimeOffset value)
	{
		DateTime utcValue = value.UtcDateTime;
		return new DateTime(TruncateToMicrosecond(utcValue.Ticks), DateTimeKind.Utc);
	}

	/// <summary>
	/// Truncates to the resolution PostgreSQL stores, preserving the offset so the zero-offset
	/// contract check still sees the value the caller sent.
	/// </summary>
	private static DateTimeOffset TruncateToMicrosecond(DateTimeOffset value)
	{
		return new DateTimeOffset(TruncateToMicrosecond(value.Ticks), value.Offset);
	}

	private static long TruncateToMicrosecond(long ticks)
	{
		return ticks - (ticks % TimeSpan.TicksPerMicrosecond);
	}

	private static CreatedEventResponse ToResponse(SportsEvent sportsEvent)
	{
		return new CreatedEventResponse(
			sportsEvent.Id,
			sportsEvent.Title,
			sportsEvent.Description,
			new DateTimeOffset(sportsEvent.StartsAtUtc),
			new DateTimeOffset(sportsEvent.EstimatedEndsAtUtc),
			new DateTimeOffset(sportsEvent.CreatedUtc),
			sportsEvent.ParticipantLimit,
			1,
			sportsEvent.AutoAccept,
			new EventVenueSummary(
				sportsEvent.VenueId,
				sportsEvent.VenueSport.Venue.Name,
				sportsEvent.VenueSport.Venue.Address),
			new EventSportSummary(
				sportsEvent.SportId,
				sportsEvent.VenueSport.Sport.Code,
				sportsEvent.VenueSport.Sport.Name));
	}

	private static IResult ToValidationProblem(ValidationResult validation)
	{
		return Results.ValidationProblem(
			validation.Errors.ToDictionary(
				pair => pair.Key,
				pair => pair.Value,
				StringComparer.Ordinal));
	}

	private static bool HasConstraint(DbUpdateException exception, string constraintName)
	{
		return exception.InnerException is PostgresException
		{
			ConstraintName: var actualConstraintName
		}
			&& string.Equals(actualConstraintName, constraintName, StringComparison.Ordinal);
	}

	private enum JoinRequestTransitionFailure
	{
		EventNotFound,
		EventEnded,
		EventCancelled,
		EventFull,
		OrganizerCannotJoin,
		RequestNotFound,
		RequestAlreadyResolved
	}

	private sealed record JoinRequestTransitionResult(
		EventJoinRequest? Request,
		JoinRequestTransitionFailure? Failure,
		bool WasCreated)
	{
		public static JoinRequestTransitionResult Succeeded(
			EventJoinRequest request,
			bool wasCreated)
		{
			return new(request, null, wasCreated);
		}

		public static JoinRequestTransitionResult Failed(
			JoinRequestTransitionFailure failure)
		{
			return new(null, failure, false);
		}
	}
	#endregion
}
