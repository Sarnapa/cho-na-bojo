# Firebase Messaging Android Binding Spike

## Outcome

- Package: `Xamarin.Firebase.Messaging` `125.1.1.1`
- Target asset: `net10.0-android36.0`
- Chosen registration mode: Firebase Installation ID (FID)
- Manifest metadata `firebase_messaging_installation_id_enabled`: present and set to `true`
- Legacy token callback `OnNewToken(string)`: available in the binding but deliberately unused

## Compile-verified binding surface

The Android build and reflection over the resolved binding confirmed these generated signatures:

```text
Android.Gms.Tasks.Task FirebaseMessaging.Register()
Android.Gms.Tasks.Task FirebaseMessaging.Unregister()
void FirebaseMessagingService.OnRegistered(string)
void FirebaseMessagingService.OnUnregistered(string)
void FirebaseMessagingService.OnNewToken(string)
void FirebaseMessagingService.OnMessageReceived(RemoteMessage)
```

`ChoNaBojoMessagingService` uses `OnRegistered(string)` and keeps the FID metadata enabled. The callback logs only that a non-empty identifier was received and its length; it does not expose the raw identifier.

## Build compatibility note

The Firebase package exposed an existing AndroidX graph mismatch: MAUI resolved `Xamarin.AndroidX.Fragment` `1.9.0` while `Xamarin.AndroidX.Fragment.Ktx` remained at `1.8.8.1`, causing duplicate `FragmentKt` Java classes during D8 compilation. Pinning `Xamarin.AndroidX.Fragment.Ktx` to `1.9.0` in the Android-only item group aligns the pair and restores the Android build.
