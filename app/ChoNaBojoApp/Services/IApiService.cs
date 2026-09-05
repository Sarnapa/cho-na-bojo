using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Events;
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

  Task<VenueCatalogResult> GetVenuesAsync(CancellationToken cancellationToken);

  Task<SportCatalogResult> GetSportsAsync(CancellationToken cancellationToken);

  Task<CreateEventResult> CreateEventAsync(
	  CreateEventRequest request,
	  CancellationToken cancellationToken);
}
