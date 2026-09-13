using System.Net.Http.Headers;
using ChoNaBojo.Contracts.Enums;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace ChoNaBojo.Server.IntegrationTests.Infrastructure;

internal sealed class EventScenarioBuilder(PostgisFixture fixture)
{
	#region Public methods
	public async Task<EventScenario> BuildAsync()
	{
		await fixture.ResetAsync();

		DateTime nowUtc = DateTime.UtcNow;
		TestIdentity[] identities =
		[
			CreateIdentity("organizer", 1),
			CreateIdentity("accepted", 2),
			CreateIdentity("accepted-peer", 3),
			CreateIdentity("pending", 4),
			CreateIdentity("rejected", 5),
			CreateIdentity("removed", 6),
			CreateIdentity("left", 7),
			CreateIdentity("stranger", 8),
			CreateIdentity("foreign-organizer", 9),
			CreateIdentity("foreign-requester", 10)
		];
		Dictionary<string, TestIdentity> byRole = identities.ToDictionary(
			identity => identity.Role,
			StringComparer.Ordinal);

		var venue = new Venue
		{
			Id = EventScenario.VenueId,
			Name = "Privacy Matrix Venue",
			Address = "Fixture Street 1",
			Description = "Disposable integration venue",
			Location = new Point(21.0122, 52.2297) { SRID = 4326 }
		};
		var foreignVenue = new Venue
		{
			Id = EventScenario.ForeignVenueId,
			Name = "Foreign Matrix Venue",
			Address = "Fixture Street 2",
			Description = "Disposable foreign venue",
			Location = new Point(21.0222, 52.2397) { SRID = 4326 }
		};

		SportsEvent activeEvent = CreateEvent(
			EventScenario.ActiveEventId,
			byRole["organizer"].UserId,
			EventScenario.VenueId,
			"Privacy matrix football",
			"Safe evening practice",
			nowUtc.AddHours(1),
			nowUtc.AddHours(3));
		SportsEvent closedEvent = CreateEvent(
			EventScenario.ClosedEventId,
			byRole["organizer"].UserId,
			EventScenario.VenueId,
			"Closed privacy matrix",
			"Finished practice",
			nowUtc.AddHours(-3),
			nowUtc.AddHours(-2),
			EventStatus.Closed,
			nowUtc.AddHours(-2));
		SportsEvent cancelledEvent = CreateEvent(
			EventScenario.CancelledEventId,
			byRole["organizer"].UserId,
			EventScenario.VenueId,
			"Cancelled privacy matrix",
			"Cancelled practice",
			nowUtc.AddHours(2),
			nowUtc.AddHours(4),
			EventStatus.Cancelled,
			nowUtc.AddMinutes(-10));
		SportsEvent foreignEvent = CreateEvent(
			EventScenario.ForeignEventId,
			byRole["foreign-organizer"].UserId,
			EventScenario.ForeignVenueId,
			"Foreign event marker",
			"Foreign event description",
			nowUtc.AddHours(2),
			nowUtc.AddHours(4));

		var requests = new List<EventJoinRequest>
		{
			CreateRequest(byRole["accepted"], activeEvent.Id, EventJoinRequestStatus.Accepted, nowUtc, 2),
			CreateRequest(byRole["accepted-peer"], activeEvent.Id, EventJoinRequestStatus.Accepted, nowUtc, 3),
			CreateRequest(byRole["pending"], activeEvent.Id, EventJoinRequestStatus.Pending, nowUtc, 4),
			CreateRequest(byRole["rejected"], activeEvent.Id, EventJoinRequestStatus.Rejected, nowUtc, 5),
			CreateRequest(byRole["removed"], activeEvent.Id, EventJoinRequestStatus.Removed, nowUtc, 6),
			CreateRequest(byRole["left"], activeEvent.Id, EventJoinRequestStatus.Left, nowUtc, 7),
			new()
			{
				Id = EventScenario.ClosedAcceptedRequestId,
				SportsEventId = closedEvent.Id,
				RequesterUserId = byRole["accepted"].UserId,
				Status = EventJoinRequestStatus.Accepted,
				CreatedUtc = nowUtc.AddDays(-2),
				UpdatedUtc = nowUtc.AddDays(-2).AddMinutes(5)
			},
			new()
			{
				Id = EventScenario.CancelledAcceptedRequestId,
				SportsEventId = cancelledEvent.Id,
				RequesterUserId = byRole["accepted"].UserId,
				Status = EventJoinRequestStatus.Cancelled,
				CreatedUtc = nowUtc.AddDays(-1),
				UpdatedUtc = nowUtc.AddMinutes(-10)
			},
			CreateRequest(
				byRole["foreign-requester"],
				foreignEvent.Id,
				EventJoinRequestStatus.Pending,
				nowUtc,
				10)
		};

		await using AsyncServiceScope scope =
			fixture.ApiFactory.Services.CreateAsyncScope();
		var dbContext =
			scope.ServiceProvider.GetRequiredService<ChoNaBojoContext>();
		dbContext.Users.AddRange(identities.Select(identity => identity.User));
		dbContext.Venues.AddRange(venue, foreignVenue);
		dbContext.VenueSports.AddRange(
			new VenueSport { VenueId = venue.Id, SportId = EventScenario.SportId },
			new VenueSport { VenueId = foreignVenue.Id, SportId = EventScenario.SportId });
		dbContext.SportsEvents.AddRange(
			activeEvent,
			closedEvent,
			cancelledEvent,
			foreignEvent);
		dbContext.EventJoinRequests.AddRange(requests);
		await dbContext.SaveChangesAsync();

		return new EventScenario(fixture, byRole);
	}
	#endregion

	#region Private methods
	private static TestIdentity CreateIdentity(string role, int sequence)
	{
		Guid userId = Guid.Parse($"10000000-0000-0000-0000-{sequence:000000000000}");
		Guid requestId = Guid.Parse($"20000000-0000-0000-0000-{sequence:000000000000}");
		string marker = role.Replace("-", string.Empty, StringComparison.Ordinal);
		var user = new User
		{
			Id = userId,
			LoginEmail = $"{marker}.login@fixture.invalid",
			NormalizedLoginEmail = $"{marker}.login@fixture.invalid".ToUpperInvariant(),
			PasswordHash = $"password-hash-{marker}-private",
			ContactPhone = $"+4855500{sequence:0000}",
			ContactEmail = $"{marker}.contact@fixture.invalid",
			CommunicatorPlatform = CommunicatorPlatform.Messenger,
			CommunicatorHandle = $"fixture-{marker}-handle",
			CreatedUtc = DateTime.UtcNow.AddDays(-30),
			UpdatedUtc = DateTime.UtcNow.AddDays(-30)
		};

		return new TestIdentity(role, userId, requestId, user);
	}

	private static SportsEvent CreateEvent(
		Guid id,
		Guid organizerId,
		int venueId,
		string title,
		string description,
		DateTime startsAtUtc,
		DateTime endsAtUtc,
		EventStatus status = EventStatus.Active,
		DateTime? statusChangedUtc = null)
	{
		return new SportsEvent
		{
			Id = id,
			OrganizerUserId = organizerId,
			ClientRequestId = Guid.NewGuid(),
			VenueId = venueId,
			SportId = EventScenario.SportId,
			Title = title,
			Description = description,
			StartsAtUtc = startsAtUtc,
			EstimatedEndsAtUtc = endsAtUtc,
			CreatedUtc = DateTime.UtcNow.AddDays(-1),
			ParticipantLimit = 12,
			AutoAccept = false,
			Status = status,
			StatusChangedUtc = statusChangedUtc
		};
	}

	private static EventJoinRequest CreateRequest(
		TestIdentity identity,
		Guid eventId,
		EventJoinRequestStatus status,
		DateTime nowUtc,
		int sequence)
	{
		return new EventJoinRequest
		{
			Id = identity.RequestId,
			SportsEventId = eventId,
			RequesterUserId = identity.UserId,
			Status = status,
			CreatedUtc = nowUtc.AddHours(-sequence),
			UpdatedUtc = status == EventJoinRequestStatus.Pending
				? null
				: nowUtc.AddMinutes(-sequence)
		};
	}
	#endregion
}

internal sealed class EventScenario(
	PostgisFixture fixture,
	IReadOnlyDictionary<string, TestIdentity> identities)
{
	public const int VenueId = 91001;
	public const int ForeignVenueId = 91002;
	public const int SportId = 1;
	public static readonly Guid ActiveEventId =
		Guid.Parse("30000000-0000-0000-0000-000000000001");
	public static readonly Guid ClosedEventId =
		Guid.Parse("30000000-0000-0000-0000-000000000002");
	public static readonly Guid CancelledEventId =
		Guid.Parse("30000000-0000-0000-0000-000000000003");
	public static readonly Guid ForeignEventId =
		Guid.Parse("30000000-0000-0000-0000-000000000004");
	public static readonly Guid MissingEventId =
		Guid.Parse("30000000-0000-0000-0000-000000000099");
	public static readonly Guid MissingRequestId =
		Guid.Parse("20000000-0000-0000-0000-000000000099");
	public static readonly Guid ClosedAcceptedRequestId =
		Guid.Parse("21000000-0000-0000-0000-000000000002");
	public static readonly Guid CancelledAcceptedRequestId =
		Guid.Parse("22000000-0000-0000-0000-000000000002");

	public IReadOnlyDictionary<string, TestIdentity> Identities => identities;

	public TestIdentity this[string role] => identities[role];

	public HttpClient CreateClient(string role)
	{
		HttpClient client = fixture.ApiFactory.CreateHttpsClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", this[role].Token);
		return client;
	}

	public IEnumerable<PrivacyMarker> AllIdentityMarkers =>
		identities.Values.SelectMany(identity => identity.AllMarkers);

	public IEnumerable<PrivacyMarker> AllContactMarkers =>
		identities.Values.SelectMany(identity => identity.ContactMarkers);
}

internal sealed record TestIdentity(
	string Role,
	Guid UserId,
	Guid RequestId,
	User User)
{
	public string Token => TestJwtFactory.CreateToken(UserId);

	public IEnumerable<PrivacyMarker> ContactMarkers =>
	[
		new($"{Role} contact email", User.ContactEmail!),
		new($"{Role} phone", User.ContactPhone!),
		new($"{Role} communicator handle", User.CommunicatorHandle!)
	];

	public IEnumerable<PrivacyMarker> AllMarkers =>
	[
		new($"{Role} login email", User.LoginEmail),
		new($"{Role} password hash", User.PasswordHash),
		new($"{Role} user id", UserId.ToString()),
		new($"{Role} request id", RequestId.ToString()),
		.. ContactMarkers
	];
}

internal sealed record PrivacyMarker(string Name, string Value);
