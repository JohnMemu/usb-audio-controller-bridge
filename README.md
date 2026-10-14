# USB Audio & Controller Bridge

An Android companion app and Windows desktop app for forwarding a phone-connected
gamepad to a virtual Xbox 360 controller and streaming Windows loopback audio to
the phone. This is version **0.2**.

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
2. Run the Windows app. The controller receiver and audio stream start automatically.
3. Android automatically discovers the PC on the local network. If discovery is blocked,
   select **Enter address manually** and enter one of the Windows app's IPv4 addresses.
4. Start either or both Android services. The Windows app can be minimized to the system tray;
   closing its window also keeps it running there. Use the tray menu to exit.
5. Enable **Open with Windows and start in the system tray** to launch the bridge at sign-in.
6. Allow the app through Windows Firewall on the private network if prompted.

The receiver binds to all local interfaces. It does not authenticate clients, so
only use it on a trusted network.

## Network protocol

| Purpose | Transport | Port | Format |
| --- | --- | --- | --- |
| Controller | UDP | 17050 | 40-byte little-endian `USBC` packet, sequence, six floats, button/D-pad masks, reserved bytes |
| Audio | TCP | 17051 | 16-byte `AUD1` header (sample rate, channel count, PCM16 encoding), followed by little-endian signed 16-bit PCM |
| Discovery | UDP | 17052 | `USBC-DISCOVER-1` request and `USBC-HOST-1` reply |

The Windows controller receiver rejects malformed, non-finite, and stale
out-of-order packets. D-pad directions are read from Android key events and
gamepad hat axes. If controller packets stop for 500 ms, it releases all buttons
and centers the virtual controller. Audio is captured from the Windows default
playback device with a reduced capture buffer and bounded low-latency client
queues. Mono/stereo PCM and IEEE-float loopback formats are converted to PCM16;
unsupported channel counts or audio formats produce a visible startup error.

Controller input is received by Android while the app's activity has focus. The
foreground network service does not make Android deliver gamepad input while the
activity is backgrounded or the screen is off; device and OS behavior varies.
Automatic discovery requires the phone and PC to share a local network that
forwards UDP broadcast traffic. Networks that block broadcasts can use the
manual IPv4 override. Android does not automatically start forwarding or playback;
start either feature from the Android app.

The reference projects informed the feature design; their implementation code is
not copied into this project.
