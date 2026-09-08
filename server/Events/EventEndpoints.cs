using ChoNaBojo.Contracts.Consts;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Contracts.Enums;
using ChoNaBojo.Server.Auth;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.Data.Entities;
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
		if (sportsEvent.EstimatedEndsAtUtc <= nowUtc)
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
