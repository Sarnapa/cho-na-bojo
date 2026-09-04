using ChoNaBojo.Contracts.DTOs;
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
	#endregion

	#region Public methods
	public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/events", CreateEventAsync)
			.WithName("EventsCreate");

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

		ValidationResult validation = EventValidation.ValidateCreateEventRequest(
			request,
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
			StartsAtUtc = NormalizeUtcTimestamp(request.StartsAtUtc),
			EstimatedEndsAtUtc = NormalizeUtcTimestamp(request.EstimatedEndsAtUtc),
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

		return Results.Json(
			ToResponse(sportsEvent),
			statusCode: StatusCodes.Status201Created);
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
				"venue_not_found",
				"venueId",
				"The selected venue no longer exists."));
		}

		bool sportExists = await dbContext.Sports
			.AsNoTracking()
			.AnyAsync(entity => entity.Id == sportId, cancellationToken);
		if (!sportExists)
		{
			return Results.Conflict(new EventConflictResponse(
				"sport_not_found",
				"sportId",
				"The selected sport no longer exists."));
		}

		return Results.Conflict(new EventConflictResponse(
			"sport_not_supported_at_venue",
			"sportId",
			"The selected sport is not supported at this venue."));
	}

	private static DateTime NormalizeUtcTimestamp(DateTimeOffset value)
	{
		DateTime utcValue = value.UtcDateTime;
		long normalizedTicks = utcValue.Ticks - (utcValue.Ticks % TimeSpan.TicksPerMicrosecond);
		return new DateTime(normalizedTicks, DateTimeKind.Utc);
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
	#endregion
}
