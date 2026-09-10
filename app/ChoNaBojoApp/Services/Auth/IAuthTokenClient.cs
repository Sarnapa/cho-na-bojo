namespace ChoNaBojo.App.Services.Auth;

#region RefreshOutcomeStatus
public enum RefreshOutcomeStatus
{
	Success,
	RetryInProgress,
	Invalid,
	Transient
}
#endregion

#region RefreshOutcome
/// <summary>
/// Result of a refresh attempt, distinguishing the four buckets the handler must react to
/// differently (see plan Critical Implementation Details: refresh-failure classification).
/// </summary>
public sealed record RefreshOutcome
{
	#region Properties
	public RefreshOutcomeStatus Status { get; }
	public ChoNaBojo.Contracts.DTOs.AuthResponse? Response { get; }
	#endregion

	#region Constructors
	private RefreshOutcome(RefreshOutcomeStatus status, ChoNaBojo.Contracts.DTOs.AuthResponse? response)
	{
		Status = status;
		Response = response;
	}
	#endregion

	#region Public methods
	public static RefreshOutcome Success(ChoNaBojo.Contracts.DTOs.AuthResponse response)
	{
		return new(RefreshOutcomeStatus.Success, response);
	}

	public static RefreshOutcome RetryInProgress()
	{
		return new(RefreshOutcomeStatus.RetryInProgress, null);
	}

	public static RefreshOutcome Invalid()
	{
		return new(RefreshOutcomeStatus.Invalid, null);
	}

	public static RefreshOutcome Transient()
	{
		return new(RefreshOutcomeStatus.Transient, null);
	}
	#endregion
}
#endregion

#region IAuthTokenClient
/// <summary>
/// Refresh and server-logout calls, issued through the un-handled "ChoNaBojoAuth" named
/// client so a 401 during refresh can never trigger another refresh, and so
/// <see cref="AuthenticatingHttpMessageHandler"/> has no dependency on <see cref="IApiService"/>
/// (which would recurse at construction through the handled "ChoNaBojoApi" client).
/// </summary>
public interface IAuthTokenClient
{
	Task<RefreshOutcome> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

	Task LogoutAsync(
		string refreshToken,
		string? deviceRegistrationId,
		CancellationToken cancellationToken);
}
#endregion
