using Android.App;
using Android.Content;
using Android.Util;
using Firebase.Messaging;

namespace ChoNaBojo.App.Platforms.Android.Push;

[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class ChoNaBojoMessagingService: FirebaseMessagingService
{
	#region Private Constants
	private const string LogTag = "ChoNaBojo.Push";
	#endregion

	#region Overrides
	public override void OnRegistered(string installationId)
	{
		Log.Info(LogTag, "Firebase registration identifier received (length {0}).", installationId.Length);
	}

	public override void OnUnregistered(string installationId)
	{
		Log.Info(LogTag, "Firebase registration identifier removed (length {0}).", installationId.Length);
	}

	public override void OnMessageReceived(RemoteMessage message)
	{
		Log.Info(LogTag, "Firebase message received (message id present: {0}).", !string.IsNullOrWhiteSpace(message.MessageId));
	}
	#endregion
}
