using Android.App;
using Android.Runtime;
using ChoNaBojo.App.Platforms.Android.Push;

namespace ChoNaBojo.App
{
	[Application]
	public class MainApplication: MauiApplication
	{
		public MainApplication(IntPtr handle, JniHandleOwnership ownership)
			: base(handle, ownership)
		{
		}

		protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

		public override void OnCreate()
		{
			base.OnCreate();
			NotificationChannels.EnsureCreated(this);
		}
	}
}
