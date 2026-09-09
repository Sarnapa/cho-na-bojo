using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Events;
using ChoNaBojo.App.Services.Venues;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Contracts.Enums;

namespace ChoNaBojo.App.Services;

public class ApiService: IApiService
{
	#region Private fields
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	private readonly HttpClient _httpClient;
	#endregion

	#region Constructors
	public ApiService(IHttpClientFactory httpClientFactory)
	{
		_httpClient = httpClientFactory.CreateClient("ChoNaBojoApi");
	}
	#endregion

	#region Public methods
	public async Task<bool> CheckHealthAsync()
	{
		try
		{
			var response = await _httpClient.GetAsync("/health");
			return response.IsSuccessStatusCode;
		}
		catch (HttpRequestException)
		{
			return false;
		}
	}

	public Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
	{
		return PostAuthAsync("/auth/register", request, cancellationToken);
	}

	public Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
	{
		return PostAuthAsync("/auth/login", request, cancellationToken);
	}

	public async Task<CurrentUserResult> GetCurrentUserAsync(CancellationToken cancellationToken)
	{
		try
		{
			using HttpResponseMessage response = await _httpClient.GetAsync("/auth/me", cancellationToken);

			if (response.IsSuccessStatusCode)
			{
				var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(JsonOptions, cancellationToken);
				return body is null ? CurrentUserResult.Unknown() : CurrentUserResult.Success(body);
			}

			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				return CurrentUserResult.Unauthorized();
			}

			return CurrentUserResult.Unknown();
		}
		catch (HttpRequestException)
		{
			return CurrentUserResult.Network();
		}
		catch (TaskCanceledException)
		{
			return CurrentUserResult.Network();
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			// Malformed body, an HTML error page from a proxy, or contract drift — must map to a
			// typed result rather than escaping into the caller's command.
			return CurrentUserResult.Unknown();
		}
	}

	public async Task<VenueCatalogResult> GetVenuesAsync(CancellationToken cancellationToken)
	{
		try
		{
			using HttpResponseMessage response = await _httpClient.GetAsync("/api/venues", cancellationToken);

			if (response.IsSuccessStatusCode)
			{
				var body = await response.Content.ReadFromJsonAsync<List<VenueResponse>>(JsonOptions, cancellationToken);
				return body is null ? VenueCatalogResult.Unknown() : VenueCatalogResult.Success(body);
			}

			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				return VenueCatalogResult.Unauthorized();
			}

			return VenueCatalogResult.Unknown();
		}
		catch (HttpRequestException)
		{
			return VenueCatalogResult.Network();
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return VenueCatalogResult.Network();
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			// Malformed body, an HTML error page from a proxy, or contract drift — must map to a
			// typed result rather than escaping into the caller's command.
			return VenueCatalogResult.Unknown();
		}
	}

	public async Task<SportCatalogResult> GetSportsAsync(CancellationToken cancellationToken)
	{
		try
		{
			using HttpResponseMessage response = await _httpClient.GetAsync("/api/sports", cancellationToken);

			if (response.IsSuccessStatusCode)
			{
				var body = await response.Content.ReadFromJsonAsync<List<SportResponse>>(JsonOptions, cancellationToken);
				return body is null ? SportCatalogResult.Unknown() : SportCatalogResult.Success(body);
			}

			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				return SportCatalogResult.Unauthorized();
			}

			return SportCatalogResult.Unknown();
		}
		catch (HttpRequestException)
		{
			return SportCatalogResult.Network();
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return SportCatalogResult.Network();
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			// Malformed body, an HTML error page from a proxy, or contract drift — must map to a
			// typed result rather than escaping into the caller's command.
			return SportCatalogResult.Unknown();
		}
	}

	public async Task<CreateEventResult> CreateEventAsync(
		CreateEventRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
				"/api/events",
				request,
				cancellationToken);

			switch (response.StatusCode)
			{
				case HttpStatusCode.OK:
				case HttpStatusCode.Created:
					var createdEvent = await response.Content.ReadFromJsonAsync<CreatedEventResponse>(
						JsonOptions,
						cancellationToken);
					return createdEvent is null
						? CreateEventResult.Unknown()
						: CreateEventResult.Success(
							createdEvent,
							response.StatusCode == HttpStatusCode.OK);

				case HttpStatusCode.BadRequest:
					var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>(
						JsonOptions,
						cancellationToken);
					return problem?.Errors is not { } validationErrors
						? CreateEventResult.Unknown()
						: CreateEventResult.ValidationFailed(validationErrors);

				case HttpStatusCode.Conflict:
					var conflict = await response.Content.ReadFromJsonAsync<EventConflictResponse>(
						JsonOptions,
						cancellationToken);
					return conflict is null
						? CreateEventResult.Unknown()
						: CreateEventResult.ReferenceChanged(conflict);

				case HttpStatusCode.Unauthorized:
					return CreateEventResult.Unauthorized();

				default:
					return CreateEventResult.Unknown();
			}
		}
		catch (HttpRequestException)
		{
			return CreateEventResult.Network();
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return CreateEventResult.Network();
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			return CreateEventResult.Unknown();
		}
	}

	public async Task<VenueEventListResult> GetVenueEventsAsync(
		int venueId,
		EventListingQuery query,
		CancellationToken cancellationToken)
	{
		try
		{
			string path = BuildVenueEventsPath(venueId, query);
			using HttpResponseMessage response = await _httpClient.GetAsync(
				path,
				cancellationToken);

			switch (response.StatusCode)
			{
				case HttpStatusCode.OK:
					var events = await response.Content.ReadFromJsonAsync<List<EventListItemResponse>>(
						JsonOptions,
						cancellationToken);
					return events is null || events.Any(IsInvalidEventItem)
						? VenueEventListResult.Unknown()
						: VenueEventListResult.Success(events);

				case HttpStatusCode.BadRequest:
					var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>(
						JsonOptions,
						cancellationToken);
					return problem?.Errors is not { } validationErrors
						? VenueEventListResult.Unknown()
						: VenueEventListResult.ValidationFailed(validationErrors);

				case HttpStatusCode.Conflict:
					var conflict = await response.Content.ReadFromJsonAsync<EventConflictResponse>(
						JsonOptions,
						cancellationToken);
					return conflict is null
						? VenueEventListResult.Unknown()
						: VenueEventListResult.ReferenceChanged(conflict);

				case HttpStatusCode.Unauthorized:
					return VenueEventListResult.Unauthorized();

				default:
					return VenueEventListResult.Unknown();
			}
		}
		catch (HttpRequestException)
		{
			return VenueEventListResult.Network();
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return VenueEventListResult.Network();
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			return VenueEventListResult.Unknown();
		}
	}

	public async Task<JoinEventResult> RequestToJoinEventAsync(
		Guid eventId,
		CancellationToken cancellationToken)
	{
		try
		{
			using HttpResponseMessage response = await _httpClient.PostAsync(
				$"/api/events/{eventId:D}/join-requests",
				content: null,
				cancellationToken);

			switch (response.StatusCode)
			{
				case HttpStatusCode.OK:
				case HttpStatusCode.Created:
					var joinRequest = await response.Content.ReadFromJsonAsync<JoinRequestResponse>(
						JsonOptions,
						cancellationToken);
					return joinRequest is null
						|| joinRequest.RequestId == Guid.Empty
						|| !Enum.IsDefined(joinRequest.Status)
						|| joinRequest.EventId != eventId
						? JoinEventResult.Unknown()
						: JoinEventResult.Success(
							joinRequest,
							response.StatusCode == HttpStatusCode.OK);

				case HttpStatusCode.Conflict:
					var conflict = await response.Content.ReadFromJsonAsync<EventConflictResponse>(
						JsonOptions,
						cancellationToken);
					return conflict is null
						? JoinEventResult.Unknown()
						: JoinEventResult.Conflict(conflict);

				case HttpStatusCode.Unauthorized:
					return JoinEventResult.Unauthorized();

				default:
					return JoinEventResult.Unknown();
			}
		}
		catch (HttpRequestException)
		{
			return JoinEventResult.Network();
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return JoinEventResult.Network();
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			return JoinEventResult.Unknown();
		}
	}

	public async Task<MyEventsResult> GetMyEventsAsync(
		CancellationToken cancellationToken)
	{
		try
		{
			using HttpResponseMessage response = await _httpClient.GetAsync(
				"/api/me/events",
				cancellationToken);

			switch (response.StatusCode)
			{
				case HttpStatusCode.OK:
					var myEvents = await response.Content.ReadFromJsonAsync<MyEventsResponse>(
						JsonOptions,
						cancellationToken);
					return myEvents is null
						|| myEvents.OrganizedEvents.Any(IsInvalidOrganizedEvent)
						|| myEvents.RequestedEvents.Any(IsInvalidRequestedEvent)
						? MyEventsResult.Unknown()
						: MyEventsResult.Success(myEvents);

				case HttpStatusCode.Unauthorized:
					return MyEventsResult.Unauthorized();

				default:
					return MyEventsResult.Unknown();
			}
		}
		catch (HttpRequestException)
		{
			return MyEventsResult.Network();
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return MyEventsResult.Network();
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			return MyEventsResult.Unknown();
		}
	}

	public async Task<EventJoinRequestQueueResult> GetEventJoinRequestsAsync(
		Guid eventId,
		CancellationToken cancellationToken)
	{
		try
		{
			using HttpResponseMessage response = await _httpClient.GetAsync(
				$"/api/events/{eventId:D}/join-requests",
				cancellationToken);

			switch (response.StatusCode)
			{
				case HttpStatusCode.OK:
					var requests = await response.Content.ReadFromJsonAsync<
						List<EventJoinRequestQueueItemResponse>>(
							JsonOptions,
							cancellationToken);
					return requests is null || requests.Any(IsInvalidJoinRequestQueueItem)
						? EventJoinRequestQueueResult.Unknown()
						: EventJoinRequestQueueResult.Success(requests);

				case HttpStatusCode.NotFound:
					var notFound = await response.Content.ReadFromJsonAsync<EventConflictResponse>(
						JsonOptions,
						cancellationToken);
					return notFound is null
						? EventJoinRequestQueueResult.Unknown()
						: EventJoinRequestQueueResult.NotFound(notFound);

				case HttpStatusCode.Unauthorized:
					return EventJoinRequestQueueResult.Unauthorized();

				default:
					return EventJoinRequestQueueResult.Unknown();
			}
		}
		catch (HttpRequestException)
		{
			return EventJoinRequestQueueResult.Network();
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return EventJoinRequestQueueResult.Network();
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			return EventJoinRequestQueueResult.Unknown();
		}
	}

	public Task<ResolveJoinRequestResult> AcceptEventJoinRequestAsync(
		Guid eventId,
		Guid requestId,
		CancellationToken cancellationToken)
	{
		return ResolveEventJoinRequestAsync(
			eventId,
			requestId,
			"accept",
			EventJoinRequestStatus.Accepted,
			cancellationToken);
	}

	public Task<ResolveJoinRequestResult> RejectEventJoinRequestAsync(
		Guid eventId,
		Guid requestId,
		CancellationToken cancellationToken)
	{
		return ResolveEventJoinRequestAsync(
			eventId,
			requestId,
			"reject",
			EventJoinRequestStatus.Rejected,
			cancellationToken);
	}
	#endregion

	#region Private methods
	private static string BuildVenueEventsPath(int venueId, EventListingQuery query)
	{
		var values = new List<string>();
		if (query.SportId.HasValue)
		{
			values.Add(
				$"sportId={query.SportId.Value.ToString(CultureInfo.InvariantCulture)}");
		}

		AddUtcQueryValue(values, "availableFromUtc", query.AvailableFromUtc);
		AddUtcQueryValue(values, "availableToUtc", query.AvailableToUtc);

		string queryString = values.Count == 0
			? string.Empty
			: $"?{string.Join("&", values)}";
		return $"/api/venues/{venueId.ToString(CultureInfo.InvariantCulture)}/events{queryString}";
	}

	private static void AddUtcQueryValue(
		ICollection<string> values,
		string name,
		DateTimeOffset? value)
	{
		if (!value.HasValue)
		{
			return;
		}

		string serializedValue = value.Value
			.ToUniversalTime()
			.ToString("O", CultureInfo.InvariantCulture);
		values.Add($"{name}={Uri.EscapeDataString(serializedValue)}");
	}

	private static bool IsInvalidEventItem(EventListItemResponse item)
	{
		return item.EventId == Guid.Empty
			|| string.IsNullOrWhiteSpace(item.Title)
			|| item.Sport is null
			|| item.Sport.Id <= 0
			|| string.IsNullOrWhiteSpace(item.Sport.Name)
			|| item.CurrentUserRequestStatus is { } status
				&& !Enum.IsDefined(status);
	}

	private static bool IsInvalidOrganizedEvent(OrganizedEventResponse item)
	{
		return item.EventId == Guid.Empty
			|| string.IsNullOrWhiteSpace(item.Title)
			|| item.Venue is null
			|| item.Venue.Id <= 0
			|| string.IsNullOrWhiteSpace(item.Venue.Name)
			|| item.Sport is null
			|| item.Sport.Id <= 0
			|| string.IsNullOrWhiteSpace(item.Sport.Name)
			|| item.ParticipantCount < 1
			|| item.ParticipantCount > item.ParticipantLimit
			|| item.PendingRequestCount < 0;
	}

	private static bool IsInvalidRequestedEvent(RequestedEventResponse item)
	{
		return item.EventId == Guid.Empty
			|| item.JoinRequestId == Guid.Empty
			|| string.IsNullOrWhiteSpace(item.Title)
			|| item.Venue is null
			|| item.Venue.Id <= 0
			|| string.IsNullOrWhiteSpace(item.Venue.Name)
			|| item.Sport is null
			|| item.Sport.Id <= 0
			|| string.IsNullOrWhiteSpace(item.Sport.Name)
			|| item.ParticipantCount < 1
			|| item.ParticipantCount > item.ParticipantLimit
			|| !Enum.IsDefined(item.Status);
	}

	private static bool IsInvalidJoinRequestQueueItem(
		EventJoinRequestQueueItemResponse item)
	{
		return item.RequestId == Guid.Empty
			|| string.IsNullOrWhiteSpace(item.RequesterDisplayKey)
			|| !Enum.IsDefined(item.Status);
	}

	private async Task<ResolveJoinRequestResult> ResolveEventJoinRequestAsync(
		Guid eventId,
		Guid requestId,
		string action,
		EventJoinRequestStatus expectedStatus,
		CancellationToken cancellationToken)
	{
		try
		{
			using HttpResponseMessage response = await _httpClient.PostAsync(
				$"/api/events/{eventId:D}/join-requests/{requestId:D}/{action}",
				content: null,
				cancellationToken);

			switch (response.StatusCode)
			{
				case HttpStatusCode.OK:
					var joinRequest = await response.Content.ReadFromJsonAsync<JoinRequestResponse>(
						JsonOptions,
						cancellationToken);
					return joinRequest is null
						|| joinRequest.EventId != eventId
						|| joinRequest.RequestId != requestId
						|| joinRequest.Status != expectedStatus
						? ResolveJoinRequestResult.Unknown()
						: ResolveJoinRequestResult.Success(joinRequest);

				case HttpStatusCode.NotFound:
					var notFound = await response.Content.ReadFromJsonAsync<EventConflictResponse>(
						JsonOptions,
						cancellationToken);
					return notFound is null
						? ResolveJoinRequestResult.Unknown()
						: ResolveJoinRequestResult.NotFound(notFound);

				case HttpStatusCode.Conflict:
					var conflict = await response.Content.ReadFromJsonAsync<EventConflictResponse>(
						JsonOptions,
						cancellationToken);
					return conflict is null
						? ResolveJoinRequestResult.Unknown()
						: ResolveJoinRequestResult.Conflict(conflict);

				case HttpStatusCode.Unauthorized:
					return ResolveJoinRequestResult.Unauthorized();

				default:
					return ResolveJoinRequestResult.Unknown();
			}
		}
		catch (HttpRequestException)
		{
			return ResolveJoinRequestResult.Network();
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return ResolveJoinRequestResult.Network();
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			return ResolveJoinRequestResult.Unknown();
		}
	}

	private async Task<AuthResult> PostAuthAsync<TRequest>(string path, TRequest request, CancellationToken cancellationToken)
	{
		try
		{
			using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(path, request, cancellationToken);

			if (response.IsSuccessStatusCode)
			{
				var body = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions, cancellationToken);
				return body is null ? AuthResult.Unknown() : AuthResult.Success(body);
			}

			switch (response.StatusCode)
			{
				case HttpStatusCode.BadRequest:
					var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>(JsonOptions, cancellationToken);
					return problem is null
						? AuthResult.Unknown()
						: AuthResult.ValidationFailed(problem.Errors);

				case HttpStatusCode.Unauthorized:
					return AuthResult.Unauthorized();

				case HttpStatusCode.Conflict:
					var conflict = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
					string message = conflict.TryGetProperty("message", out var messageElement)
						? messageElement.GetString() ?? "Conflict."
						: "Conflict.";
					return AuthResult.Conflict(message);

				default:
					return AuthResult.Unknown();
			}
		}
		catch (HttpRequestException)
		{
			return AuthResult.Network();
		}
		catch (TaskCanceledException)
		{
			return AuthResult.Network();
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			// Malformed body, an HTML error page from a proxy, or contract drift — must map to a
			// typed result rather than escaping into the caller's command.
			return AuthResult.Unknown();
		}
	}
	#endregion
}
