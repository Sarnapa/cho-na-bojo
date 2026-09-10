using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Events;
using ChoNaBojo.App.Services.Push;
using ChoNaBojo.App.Services.Venues;
using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.App.Services;

/// <summary>
/// Protected/business API calls, made through the "ChoNaBojoApi" named client (bearer +
/// transparent refresh via <see cref="AuthenticatingHttpMessageHandler"/>). Register and
/// login are unauthenticated but still typed here since they share the same client and DTOs.
/// </summary>
public interface IApiService
{
  Task<bool> CheckHealthAsync();

  Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

  Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

  Task<CurrentUserResult> GetCurrentUserAsync(CancellationToken cancellationToken);

  Task<RegisterPushInstallationResult> RegisterPushInstallationAsync(
	  RegisterPushInstallationRequest request,
	  CancellationToken cancellationToken);

  Task<VenueCatalogResult> GetVenuesAsync(CancellationToken cancellationToken);

  Task<SportCatalogResult> GetSportsAsync(CancellationToken cancellationToken);

  Task<CreateEventResult> CreateEventAsync(
	  CreateEventRequest request,
	  CancellationToken cancellationToken);

  Task<VenueEventListResult> GetVenueEventsAsync(
	  int venueId,
	  EventListingQuery query,
	  CancellationToken cancellationToken);

  Task<JoinEventResult> RequestToJoinEventAsync(
	  Guid eventId,
	  CancellationToken cancellationToken);

  Task<MyEventsResult> GetMyEventsAsync(CancellationToken cancellationToken);

  Task<EventJoinRequestQueueResult> GetEventJoinRequestsAsync(
	  Guid eventId,
	  CancellationToken cancellationToken);

  Task<EventContactsResult> GetEventContactsAsync(
	  Guid eventId,
	  CancellationToken cancellationToken);

  Task<ResolveJoinRequestResult> AcceptEventJoinRequestAsync(
	  Guid eventId,
	  Guid requestId,
	  CancellationToken cancellationToken);

  Task<ResolveJoinRequestResult> RejectEventJoinRequestAsync(
	  Guid eventId,
	  Guid requestId,
	  CancellationToken cancellationToken);
}
