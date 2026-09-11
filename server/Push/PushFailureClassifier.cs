using System.Net;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;

namespace ChoNaBojo.Server.Push;

public static class PushFailureClassifier
{
	#region Public constants
	public const int MaxAttempts = 5;
	#endregion

	#region Private constants
	private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(15);
	private static readonly TimeSpan QuotaExceededMinimumBackoff = TimeSpan.FromMinutes(1);
	#endregion

	#region Public methods
	public static PushSendOutcome Classify(
		FirebaseMessagingException exception,
		DateTimeOffset nowUtc)
	{
		ArgumentNullException.ThrowIfNull(exception);

		string errorCode = exception.MessagingErrorCode?.ToString()
			?? exception.ErrorCode.ToString();
		HttpStatusCode? statusCode = exception.HttpResponse?.StatusCode;

		if (exception.MessagingErrorCode == MessagingErrorCode.Unregistered
			|| statusCode == HttpStatusCode.NotFound)
		{
			return PushSendOutcome.Unregistered(errorCode);
		}

		if (IsRetryable(exception, statusCode))
		{
			TimeSpan? retryAfter = ReadRetryAfter(exception, nowUtc);
			if (exception.MessagingErrorCode == MessagingErrorCode.QuotaExceeded
				|| statusCode == HttpStatusCode.TooManyRequests)
			{
				retryAfter = Max(retryAfter, QuotaExceededMinimumBackoff);
			}

			return PushSendOutcome.Retryable(errorCode, retryAfter);
		}

		return PushSendOutcome.Terminal(errorCode);
	}

	public static TimeSpan CalculateRetryDelay(
		int attemptCount,
		TimeSpan? retryAfter,
		double jitter)
	{
		if (attemptCount < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(attemptCount));
		}

		if (jitter is < 0 or > 1)
		{
			throw new ArgumentOutOfRangeException(nameof(jitter));
		}

		double exponentialSeconds = BaseBackoff.TotalSeconds
			* Math.Pow(2, Math.Min(attemptCount - 1, MaxAttempts - 1));
		double jitterMultiplier = 0.8 + (jitter * 0.4);
		TimeSpan backoff = TimeSpan.FromSeconds(
			Math.Min(
				exponentialSeconds * jitterMultiplier,
				MaximumBackoff.TotalSeconds));

		if (retryAfter is null)
		{
			return backoff;
		}

		TimeSpan boundedRetryAfter = retryAfter.Value > MaximumBackoff
			? MaximumBackoff
			: retryAfter.Value;
		return boundedRetryAfter > backoff
			? boundedRetryAfter
			: backoff;
	}
	#endregion

	#region Private methods
	private static bool IsRetryable(
		FirebaseMessagingException exception,
		HttpStatusCode? statusCode)
	{
		if (exception.MessagingErrorCode is
			MessagingErrorCode.QuotaExceeded
			or MessagingErrorCode.Unavailable
			or MessagingErrorCode.Internal)
		{
			return true;
		}

		if (statusCode is
			HttpStatusCode.TooManyRequests
			or HttpStatusCode.ServiceUnavailable
			or HttpStatusCode.InternalServerError)
		{
			return true;
		}

		return exception.ErrorCode is
			ErrorCode.ResourceExhausted
			or ErrorCode.Unavailable
			or ErrorCode.Internal
			or ErrorCode.DeadlineExceeded
			or ErrorCode.Unknown;
	}

	private static TimeSpan? ReadRetryAfter(
		FirebaseMessagingException exception,
		DateTimeOffset nowUtc)
	{
		var retryAfter = exception.HttpResponse?.Headers.RetryAfter;
		if (retryAfter?.Delta is { } delta)
		{
			return delta > TimeSpan.Zero ? delta : null;
		}

		if (retryAfter?.Date is not { } retryAt)
		{
			return null;
		}

		TimeSpan delay = retryAt - nowUtc;
		return delay > TimeSpan.Zero ? delay : null;
	}

	private static TimeSpan Max(TimeSpan? left, TimeSpan right)
	{
		return left is { } value && value > right ? value : right;
	}
	#endregion
}
