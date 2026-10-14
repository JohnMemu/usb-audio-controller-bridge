# USB Audio & Controller Bridge

An Android companion app and Windows desktop app for forwarding a phone-connected
gamepad to a virtual Xbox 360 controller and streaming Windows loopback audio to
the phone. This is the first public release, version **0.1**.

## Build

- Windows: install the .NET 8 SDK and ViGEmBus, then run
  `dotnet build windows\UsbAudioController.sln`.
- Android: install Android Studio and the Android SDK, then run
  `android\gradlew.bat -p android :app:assembleDebug`.

Windows uses the Nefarius ViGEm client and NAudio. The ViGEmBus driver is required
to create the virtual controller. The Android app requires Android 10 (API 29) or
later. The generated debug APK is at
`android\app\build\outputs\apk\debug\app-debug.apk`.

## Connect

1. Connect the PC and phone to the same reachable network (USB tethering or Wi-Fi).
2. Run the Windows app. It displays the PC's active IPv4 addresses.
3. Enter the reachable PC address in Android.
4. Start either or both Android services and their corresponding Windows listener.
5. Allow the app through Windows Firewall on the private network if prompted.

The receiver binds to all local interfaces. It does not authenticate clients, so
only use it on a trusted network.

## Network protocol

| Purpose | Transport | Port | Format |
| --- | --- | --- | --- |
| Controller | UDP | 17050 | 40-byte little-endian `USBC` packet, sequence, six floats, button/D-pad masks, reserved bytes |
| Audio | TCP | 17051 | 16-byte `AUD1` header (sample rate, channel count, PCM16 encoding), followed by little-endian signed 16-bit PCM |

The Windows controller receiver rejects malformed, non-finite, and stale
out-of-order packets. If controller packets stop for 500 ms, it releases all
buttons and centers the virtual controller. Audio is captured from the Windows
default playback device. Mono/stereo PCM and IEEE-float loopback formats are
converted to PCM16; unsupported channel counts or audio formats produce a visible
startup error.

Controller input is received by Android while the app's activity has focus. The
foreground network service does not make Android deliver gamepad input while the
activity is backgrounded or the screen is off; device and OS behavior varies.
The apps do not automatically discover each other or connect when USB is plugged
in. The address is configured manually.

The reference projects informed the feature design; their implementation code is
not copied into this project.
