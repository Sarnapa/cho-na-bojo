using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChoNaBojo.Server.IntegrationTests.Events;

[Collection(IntegrationTestCollection.Name)]
public sealed class EventContactPublishGuardTests(PostgisFixture fixture) :
	IAsyncLifetime
{
	#region Private fields
	private EventScenario _scenario = null!;
	#endregion

	#region IAsyncLifetime implementation
	public async Task InitializeAsync()
	{
		_scenario = await new EventScenarioBuilder(fixture).BuildAsync();
	}

	public Task DisposeAsync()
	{
		return Task.CompletedTask;
	}
	#endregion

	#region Test methods
	[Theory]
	[InlineData("Email organizer@fixture.invalid", "Safe practice details", "title")]
	[InlineData("Safe practice", "Call +48 555 123 456 after work", "description")]
	public async Task ContactLikePublicText_IsRejectedAndNotPersisted(
		string title,
		string description,
		string expectedField)
	{
		int before = await CountEventsAsync();
		using HttpClient client = _scenario.CreateClient("organizer");
		var request = CreateRequest(title, description);

		using HttpResponseMessage response =
			await client.PostAsJsonAsync("/api/events", request);
		string body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		using JsonDocument document = JsonDocument.Parse(body);
		Assert.True(
			document.RootElement
				.GetProperty("errors")
				.TryGetProperty(expectedField, out _));
		Assert.Equal(before, await CountEventsAsync());
	}

	[Fact]
	public async Task SafeSportsText_IsPersistedAndVisibleInListing()
	{
		const string safeTitle = "Sunday football drills";
		const string safeDescription = "Bring a ball at 18:30";
		using HttpClient client = _scenario.CreateClient("organizer");
		var request = CreateRequest(safeTitle, safeDescription);

		using HttpResponseMessage response =
			await client.PostAsJsonAsync("/api/events", request);
		Assert.Equal(HttpStatusCode.Created, response.StatusCode);

		string listing = await client.GetStringAsync(
			$"/api/venues/{EventScenario.VenueId}/events");
		Assert.Contains(safeTitle, listing, StringComparison.Ordinal);
		Assert.Contains(safeDescription, listing, StringComparison.Ordinal);
		JsonPrivacyAssertions.DoesNotContainMarkers(
			$"/api/venues/{EventScenario.VenueId}/events",
			"organizer",
			listing,
			_scenario.AllContactMarkers);
	}
	#endregion

	#region Private methods
	private static CreateEventRequest CreateRequest(
		string title,
		string description)
	{
		DateTimeOffset startsAtUtc = DateTimeOffset.UtcNow.AddHours(5);
		return new CreateEventRequest(
			Guid.NewGuid(),
			EventScenario.VenueId,
			EventScenario.SportId,
			title,
			description,
			startsAtUtc,
			startsAtUtc.AddHours(2),
			10,
			AutoAccept: false);
	}

	private async Task<int> CountEventsAsync()
	{
		await using AsyncServiceScope scope =
			fixture.ApiFactory.Services.CreateAsyncScope();
		var dbContext =
			scope.ServiceProvider.GetRequiredService<ChoNaBojoContext>();
		return await dbContext.SportsEvents.CountAsync();
	}
	#endregion
}
