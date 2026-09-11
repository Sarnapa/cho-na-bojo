namespace ChoNaBojo.Server.Push;

#region PushSendResultKind enum
public enum PushSendResultKind
{
	Accepted,
	RetryableFailure,
	TerminalFailure,
	UnregisteredDestination
}
#endregion

#region PushSendOutcome record
public sealed record PushSendOutcome(
	PushSendResultKind Kind,
	string? FcmMessageId = null,
	string? ErrorCode = null,
	TimeSpan? RetryAfter = null)
{
	#region Public methods
	public static PushSendOutcome Accepted(string fcmMessageId)
	{
		return new PushSendOutcome(
			PushSendResultKind.Accepted,
			FcmMessageId: fcmMessageId);
	}

	public static PushSendOutcome Retryable(
		string errorCode,
		TimeSpan? retryAfter)
	{
		return new PushSendOutcome(
			PushSendResultKind.RetryableFailure,
			ErrorCode: errorCode,
			RetryAfter: retryAfter);
	}

	public static PushSendOutcome Terminal(string errorCode)
	{
		return new PushSendOutcome(
			PushSendResultKind.TerminalFailure,
			ErrorCode: errorCode);
	}

	public static PushSendOutcome Unregistered(string errorCode)
	{
		return new PushSendOutcome(
			PushSendResultKind.UnregisteredDestination,
			ErrorCode: errorCode);
	}
	#endregion
}
#endregion
