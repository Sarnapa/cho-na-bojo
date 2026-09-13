using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChoNaBojo.Contracts.Consts;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Contracts.Enums;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.Data.Entities;
using ChoNaBojo.Server.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Npgsql;
using Xunit.Abstractions;

namespace ChoNaBojo.Server.IntegrationTests.Events;

[Collection(IntegrationTestCollection.Name)]
public sealed class FinalSlotCapacityTests(
	PostgisFixture fixture,
	ITestOutputHelper output) : IAsyncLifetime
{
	#region Private constants
	private const int SportId = 1;
	private const int VenueId = 92001;
	#endregion

	#region Private fields
	private static readonly Guid EventId =
		Guid.Parse("40000000-0000-0000-0000-000000000001");
	private static readonly Guid OrganizerId =
		Guid.Parse("41000000-0000-0000-0000-000000000001");
	private static readonly Guid FirstContenderId =
		Guid.Parse("41000000-0000-0000-0000-000000000002");
	private static readonly Guid SecondContenderId =
		Guid.Parse("41000000-0000-0000-0000-000000000003");
	private static readonly Guid FirstRequestId =
		Guid.Parse("42000000-0000-0000-0000-000000000002");
	private static readonly Guid SecondRequestId =
		Guid.Parse("42000000-0000-0000-0000-000000000003");
	#endregion

	#region IAsyncLifetime implementation
	public Task InitializeAsync()
	{
		return fixture.ResetAsync();
	}

	public Task DisposeAsync()
	{
		return Task.CompletedTask;
	}
	#endregion

	#region Test methods
	[Fact]
	public async Task TwoManualAccepts_ForOneSlot_AcceptExactlyOneRequest()
	{
		await SeedScenarioAsync(
			autoAccept: false,
			FirstRequestId,
			SecondRequestId);
		using HttpClient firstClient = CreateClient(OrganizerId);
		using HttpClient secondClient = CreateClient(OrganizerId);
		var coordinator =
			new PostgresLockCoordinator(fixture.ConnectionString);

		using CoordinatedHttpResponses responses =
			await coordinator.CoordinateAsync(
				EventId,
				() => firstClient.PostAsync(
					AcceptRoute(FirstRequestId),
					content: null),
				() => secondClient.PostAsync(
					AcceptRoute(SecondRequestId),
					content: null));

		output.WriteLine(responses.Observation.ToDiagnosticString());
		await AssertOneSuccessAndOneFullAsync(
			responses,
			HttpStatusCode.OK);
		CapacitySnapshot state = await CaptureStateAsync();
		AssertCapacity(state);
		Assert.Equal(2, state.Requests.Count);
		Assert.Single(
			state.Requests,
			request => request.Status == EventJoinRequestStatus.Accepted);
		CapacityRequest pending = Assert.Single(
			state.Requests,
			request => request.Status == EventJoinRequestStatus.Pending);
		Assert.Null(pending.UpdatedUtc);
	}

	[Fact]
	public async Task TwoAutoAcceptJoins_ForOneSlot_CreateExactlyOneRequest()
	{
		await SeedScenarioAsync(autoAccept: true);
		using HttpClient firstClient = CreateClient(FirstContenderId);
		using HttpClient secondClient = CreateClient(SecondContenderId);
		var coordinator =
			new PostgresLockCoordinator(fixture.ConnectionString);

		using CoordinatedHttpResponses responses =
			await coordinator.CoordinateAsync(
				EventId,
				() => firstClient.PostAsync(JoinRoute(), content: null),
				() => secondClient.PostAsync(JoinRoute(), content: null));

		output.WriteLine(responses.Observation.ToDiagnosticString());
		await AssertOneSuccessAndOneFullAsync(
			responses,
			HttpStatusCode.Created);
		CapacitySnapshot state = await CaptureStateAsync();
		AssertCapacity(state);
		CapacityRequest accepted = Assert.Single(state.Requests);
		Assert.Equal(EventJoinRequestStatus.Accepted, accepted.Status);
		Assert.NotNull(accepted.UpdatedUtc);
	}

	[Fact]
	public async Task ManualAcceptAndAutoAcceptJoin_ForOneSlot_ShareCapacityLock()
	{
		await SeedScenarioAsync(autoAccept: true, FirstRequestId);
		using HttpClient organizerClient = CreateClient(OrganizerId);
		using HttpClient joiningClient = CreateClient(SecondContenderId);
		var coordinator =
			new PostgresLockCoordinator(fixture.ConnectionString);

		using CoordinatedHttpResponses responses =
			await coordinator.CoordinateAsync(
				EventId,
				() => organizerClient.PostAsync(
					AcceptRoute(FirstRequestId),
					content: null),
				() => joiningClient.PostAsync(JoinRoute(), content: null));

		output.WriteLine(responses.Observation.ToDiagnosticString());
		await AssertOneMixedSuccessAndOneFullAsync(responses);
		CapacitySnapshot state = await CaptureStateAsync();
		AssertCapacity(state);

		CapacityRequest manualRequest = Assert.Single(
			state.Requests,
			request => request.RequesterUserId == FirstContenderId);
		if (responses.First.StatusCode == HttpStatusCode.OK)
		{
			Assert.Equal(EventJoinRequestStatus.Accepted, manualRequest.Status);
			Assert.DoesNotContain(
				state.Requests,
				request => request.RequesterUserId == SecondContenderId);
		}
		else
		{
			Assert.Equal(EventJoinRequestStatus.Pending, manualRequest.Status);
			Assert.Null(manualRequest.UpdatedUtc);
			CapacityRequest autoAccepted = Assert.Single(
				state.Requests,
				request => request.RequesterUserId == SecondContenderId);
			Assert.Equal(
				EventJoinRequestStatus.Accepted,
				autoAccepted.Status);
		}
	}

	[Fact]
	public async Task ObserverTimeout_ReportsDiagnosticsAndReleasesControlLock()
	{
		await SeedScenarioAsync(autoAccept: false);
		var coordinator = new PostgresLockCoordinator(
			fixture.ConnectionString,
			timeout: TimeSpan.FromMilliseconds(50),
			pollInterval: TimeSpan.FromMilliseconds(10));

		TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(
			() => coordinator.CoordinateAsync(
				EventId,
				() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
				() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

		output.WriteLine(exception.Message);
		Assert.Contains("Control PID:", exception.Message, StringComparison.Ordinal);
		Assert.Contains(
			"PID/state/wait/query/blockers",
			exception.Message,
			StringComparison.Ordinal);
		await AssertEventLockIsAvailableAsync();
	}
	#endregion

	#region Private methods
	private async Task SeedScenarioAsync(
		bool autoAccept,
		params Guid[] pendingRequestIds)
	{
		DateTime nowUtc = DateTime.UtcNow;
		User organizer = CreateUser(OrganizerId, "organizer", nowUtc);
		User firstContender =
			CreateUser(FirstContenderId, "first", nowUtc);
		User secondContender =
			CreateUser(SecondContenderId, "second", nowUtc);
		var venue = new Venue
		{
			Id = VenueId,
			Name = "Final Slot Fixture Venue",
			Address = "Concurrency Street 1",
			Description = "Disposable capacity fixture",
			Location = new Point(21.0122, 52.2297) { SRID = 4326 }
		};
		var sportsEvent = new SportsEvent
		{
			Id = EventId,
			OrganizerUserId = OrganizerId,
			ClientRequestId = Guid.NewGuid(),
			VenueId = VenueId,
			SportId = SportId,
			Title = "Final slot football",
			Description = "Deterministic capacity race",
			StartsAtUtc = nowUtc.AddHours(1),
			EstimatedEndsAtUtc = nowUtc.AddHours(3),
			CreatedUtc = nowUtc.AddHours(-1),
			ParticipantLimit = 2,
			AutoAccept = autoAccept,
			Status = EventStatus.Active
		};

		await using AsyncServiceScope scope =
			fixture.ApiFactory.Services.CreateAsyncScope();
		var dbContext =
			scope.ServiceProvider.GetRequiredService<ChoNaBojoContext>();
		dbContext.Users.AddRange(
			organizer,
			firstContender,
			secondContender);
		dbContext.Venues.Add(venue);
		dbContext.VenueSports.Add(new VenueSport
		{
			VenueId = VenueId,
			SportId = SportId
		});
		dbContext.SportsEvents.Add(sportsEvent);
		foreach (Guid requestId in pendingRequestIds)
		{
			Guid requesterId = requestId == FirstRequestId
				? FirstContenderId
				: SecondContenderId;
			dbContext.EventJoinRequests.Add(new EventJoinRequest
			{
				Id = requestId,
				SportsEventId = EventId,
				RequesterUserId = requesterId,
				Status = EventJoinRequestStatus.Pending,
				CreatedUtc = nowUtc.AddMinutes(-10)
			});
		}

		await dbContext.SaveChangesAsync();
	}

	private static User CreateUser(
		Guid id,
		string marker,
		DateTime nowUtc)
	{
		return new User
		{
			Id = id,
			LoginEmail = $"{marker}.capacity@fixture.invalid",
			NormalizedLoginEmail =
				$"{marker}.capacity@fixture.invalid".ToUpperInvariant(),
			PasswordHash = $"capacity-hash-{marker}",
			ContactPhone = $"+4855599{marker.Length:0000}",
			CreatedUtc = nowUtc.AddDays(-1),
			UpdatedUtc = nowUtc.AddDays(-1)
		};
	}

	private HttpClient CreateClient(Guid userId)
	{
		HttpClient client = fixture.ApiFactory.CreateHttpsClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue(
				"Bearer",
				TestJwtFactory.CreateToken(userId));
		return client;
	}

	private static string JoinRoute()
	{
		return $"/api/events/{EventId}/join-requests";
	}

	private static string AcceptRoute(Guid requestId)
	{
		return $"/api/events/{EventId}/join-requests/{requestId}/accept";
	}

	private static async Task AssertOneSuccessAndOneFullAsync(
		CoordinatedHttpResponses responses,
		HttpStatusCode successStatus)
	{
		Assert.Single(
			responses.All,
			response => response.StatusCode == successStatus);
		HttpResponseMessage conflict = Assert.Single(
			responses.All,
			response => response.StatusCode == HttpStatusCode.Conflict);
		await AssertAcceptedResponseAsync(
			Assert.Single(
				responses.All,
				response => response.StatusCode == successStatus));
		await AssertEventFullAsync(conflict);
	}

	private static async Task AssertOneMixedSuccessAndOneFullAsync(
		CoordinatedHttpResponses responses)
	{
		HttpResponseMessage success = Assert.Single(
			responses.All,
			response => response.StatusCode is HttpStatusCode.OK
				or HttpStatusCode.Created);
		HttpResponseMessage conflict = Assert.Single(
			responses.All,
			response => response.StatusCode == HttpStatusCode.Conflict);
		await AssertAcceptedResponseAsync(success);
		await AssertEventFullAsync(conflict);
	}

	private static async Task AssertAcceptedResponseAsync(
		HttpResponseMessage response)
	{
		JoinRequestResponse? body =
			await response.Content.ReadFromJsonAsync<JoinRequestResponse>();
		Assert.NotNull(body);
		Assert.Equal(EventJoinRequestStatus.Accepted, body.Status);
	}

	private static async Task AssertEventFullAsync(
		HttpResponseMessage response)
	{
		EventConflictResponse? body =
			await response.Content.ReadFromJsonAsync<EventConflictResponse>();
		Assert.NotNull(body);
		Assert.Equal(EventConflictCodes.EventFull, body.Code);
	}

	private async Task<CapacitySnapshot> CaptureStateAsync()
	{
		await using AsyncServiceScope scope =
			fixture.ApiFactory.Services.CreateAsyncScope();
		var dbContext =
			scope.ServiceProvider.GetRequiredService<ChoNaBojoContext>();
		int participantLimit = await dbContext.SportsEvents
			.AsNoTracking()
			.Where(sportsEvent => sportsEvent.Id == EventId)
			.Select(sportsEvent => sportsEvent.ParticipantLimit)
			.SingleAsync();
		CapacityRequest[] requests = await dbContext.EventJoinRequests
			.AsNoTracking()
			.Where(request => request.SportsEventId == EventId)
			.OrderBy(request => request.Id)
			.Select(request => new CapacityRequest(
				request.Id,
				request.RequesterUserId,
				request.Status,
				request.UpdatedUtc))
			.ToArrayAsync();
		return new CapacitySnapshot(participantLimit, requests);
	}

	private static void AssertCapacity(CapacitySnapshot state)
	{
		int acceptedCount = state.Requests.Count(
			request => request.Status == EventJoinRequestStatus.Accepted);
		Assert.Equal(1, acceptedCount);
		Assert.Equal(2, 1 + acceptedCount);
		Assert.Equal(state.ParticipantLimit, 1 + acceptedCount);
	}

	private async Task AssertEventLockIsAvailableAsync()
	{
		await using var connection =
			new NpgsqlConnection(fixture.ConnectionString);
		await connection.OpenAsync();
		await using NpgsqlTransaction transaction =
			await connection.BeginTransactionAsync();
		await using NpgsqlCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText =
			"""
			SET LOCAL lock_timeout = '250ms';
			SELECT 1
			FROM "SportsEvents"
			WHERE "Id" = @eventId
			FOR UPDATE;
			""";
		command.Parameters.AddWithValue("eventId", EventId);
		object? result = await command.ExecuteScalarAsync();
		Assert.Equal(1, Convert.ToInt32(result));
		await transaction.RollbackAsync();
	}
	#endregion

	#region Private records
	private sealed record CapacitySnapshot(
		int ParticipantLimit,
		IReadOnlyList<CapacityRequest> Requests);

	private sealed record CapacityRequest(
		Guid RequestId,
		Guid RequesterUserId,
		EventJoinRequestStatus Status,
		DateTime? UpdatedUtc);
	#endregion
}
