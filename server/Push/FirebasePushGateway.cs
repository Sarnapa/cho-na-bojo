using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace ChoNaBojo.Server.Push;

public sealed class FirebasePushGateway : IPushGateway, IDisposable
{
	#region Private constants
	private const string FirebaseAppName = "ChoNaBojo.Push";
	private static readonly TimeSpan MessageTimeToLive = TimeSpan.FromMinutes(30);
	#endregion

	#region Private fields
	private readonly FirebaseApp _firebaseApp;
	private readonly FirebaseMessaging _firebaseMessaging;
	#endregion

	#region Constructors
	public FirebasePushGateway(IOptions<FirebasePushOptions> optionsAccessor)
	{
		FirebasePushOptions options = optionsAccessor.Value;
		_firebaseApp = FirebaseApp.Create(
			new AppOptions
			{
				Credential = CredentialFactory
					.FromJson<ServiceAccountCredential>(options.ServiceAccountJson)
					.ToGoogleCredential(),
				ProjectId = options.ProjectId
			},
			FirebaseAppName);
		_firebaseMessaging = FirebaseMessaging.GetMessaging(_firebaseApp);
	}
	#endregion

	#region Public methods
	public async Task<PushSendOutcome> SendAsync(
		PushMessage message,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(message);

		var firebaseMessage = new Message
		{
			Fid = message.DeviceRegistrationId,
			Notification = new Notification
			{
				Title = message.Title,
				Body = message.Body
			},
			Data = message.Data,
			Android = new AndroidConfig
			{
				Priority = Priority.High,
				TimeToLive = MessageTimeToLive,
				Notification = new AndroidNotification
				{
					// FirebaseAdmin 3.6.0 serializes the default DateTime.MinValue as event_time.
					// Set the real send time so Android does not render the notification as millennia old.
					EventTimestamp = DateTime.UtcNow,
					Tag = message.NotificationId.ToString("D")
				}
			}
		};

		try
		{
			string fcmMessageId = await _firebaseMessaging.SendAsync(
				firebaseMessage,
				cancellationToken);
			return PushSendOutcome.Accepted(fcmMessageId);
		}
		catch (FirebaseMessagingException exception)
		{
			return PushFailureClassifier.Classify(
				exception,
				DateTimeOffset.UtcNow);
		}
		catch (ArgumentException)
		{
			return PushSendOutcome.Terminal("InvalidMessage");
		}
	}

	public void Dispose()
	{
		_firebaseApp.Delete();
	}
	#endregion
}
