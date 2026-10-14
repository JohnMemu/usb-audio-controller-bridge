package com.glauberoller.usbpinton

import android.Manifest
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.view.InputDevice
import android.view.KeyEvent
import android.view.MotionEvent
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import com.glauberoller.usbpinton.network.PcDiscovery
import com.glauberoller.usbpinton.audio.AudioPlaybackService
import com.glauberoller.usbpinton.controller.ControllerForwardService
import com.glauberoller.usbpinton.input.SharedControllerState
import com.glauberoller.usbpinton.ui.theme.UsbAudioReceiverTheme
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext

class MainActivity : ComponentActivity() {

    private val requestNotificationPermission = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (Build.VERSION.SDK_INT >= 33 &&
            checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED
        ) {
            requestNotificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
        }

        setContent {
            UsbAudioReceiverTheme {
                val preferences = remember { getSharedPreferences("bridge", MODE_PRIVATE) }
                val savedManualHost = remember { preferences.getString("manualHost", "") ?: "" }
                var manualHost by remember { mutableStateOf(savedManualHost) }
                var detectedHost by remember { mutableStateOf("") }
                var manualMode by remember { mutableStateOf(savedManualHost.isNotBlank()) }
                var discoveryStatus by remember { mutableStateOf("Searching for the Windows bridge…") }
                val host = if (manualMode) manualHost.trim() else detectedHost
                var controllerStarted by remember { mutableStateOf(false) }
                var audioStarted by remember { mutableStateOf(false) }
                var validationError by remember { mutableStateOf<String?>(null) }
                val controllerStatus by BridgeStatus.controller.collectAsState()
                val audioStatus by BridgeStatus.audio.collectAsState()

                LaunchedEffect(manualMode) {
                    if (!manualMode) {
                        while (isActive) {
                            val found = withContext(Dispatchers.IO) { PcDiscovery.discover() }
                            if (found != null) {
                                detectedHost = found
                                discoveryStatus = "Windows PC found automatically at $found."
                                delay(5_000)
                            } else {
                                discoveryStatus = if (detectedHost.isBlank()) {
                                    "Searching for the Windows bridge…"
                                } else {
                                    "No discovery reply; last PC found at $detectedHost."
                                }
                                delay(1_500)
                            }
                        }
                    }
                }

                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background
                ) {
                    Column(
                        modifier = Modifier
                            .fillMaxSize()
                            .verticalScroll(rememberScrollState())
                            .padding(20.dp),
                        verticalArrangement = Arrangement.spacedBy(16.dp)
                    ) {
                        Text("USB Audio & Controller Bridge", style = MaterialTheme.typography.headlineSmall)
                        Text("Connect this phone and the PC to the same USB-tethered or Wi-Fi network.")
                        Text(
                            if (manualMode) "Manual PC address: ${host.ifBlank { "not entered" }}"
                            else discoveryStatus
                        )
                        if (manualMode) {
                            OutlinedTextField(
                                value = manualHost,
                                onValueChange = {
                                    manualHost = it
                                    validationError = null
                                },
                                label = { Text("Windows PC IPv4 address") },
                                singleLine = true,
                                isError = validationError != null,
                                supportingText = {
                                    Text(validationError ?: "Controller UDP 17050 · PC audio TCP 17051")
                                },
                                modifier = Modifier.fillMaxWidth()
                            )
                            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                Button(
                                    onClick = {
                                        if (isValidIpv4(manualHost.trim())) {
                                            preferences.edit().putString("manualHost", manualHost.trim()).apply()
                                            validationError = null
                                        } else {
                                            validationError = "Enter a valid IPv4 address."
                                        }
                                    }
                                ) {
                                    Text("Save address")
                                }
                                Button(
                                    onClick = {
                                        preferences.edit().remove("manualHost").apply()
                                        manualHost = ""
                                        manualMode = false
                                        validationError = null
                                    }
                                ) {
                                    Text("Use automatic discovery")
                                }
                            }
                        } else {
                            Button(
                                onClick = {
                                    manualMode = true
                                    validationError = null
                                }
                            ) {
                                Text("Enter address manually")
                            }
                        }
                        Card(modifier = Modifier.fillMaxWidth()) {
                            Column(
                                modifier = Modifier.padding(16.dp),
                                verticalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                Text("Controller forwarding", style = MaterialTheme.typography.titleMedium)
                                Text(controllerStatus)
                                Button(
                                    onClick = {
                                        val address = host.trim()
                                        if (address.isBlank()) {
                                            validationError = "Waiting for automatic PC discovery. You can enter an address manually."
                                        } else {
                                            ContextCompat.startForegroundService(
                                                this@MainActivity,
                                                Intent(this@MainActivity, ControllerForwardService::class.java)
                                                    .putExtra(ControllerForwardService.EXTRA_HOST, address)
                                            )
                                            controllerStarted = true
                                        }
                                    },
                                    enabled = !controllerStarted
                                ) {
                                    Text("Start controller")
                                }
                                Button(
                                    onClick = {
                                        stopService(Intent(this@MainActivity, ControllerForwardService::class.java))
                                        controllerStarted = false
                                    },
                                    enabled = controllerStarted
                                ) {
                                    Text("Stop controller")
                                }
                            }
                        }
                        Card(modifier = Modifier.fillMaxWidth()) {
                            Column(
                                modifier = Modifier.padding(16.dp),
                                verticalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                Text("PC audio playback", style = MaterialTheme.typography.titleMedium)
                                Text(audioStatus)
                                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                    Button(
                                        onClick = {
                                            val address = host.trim()
                                            if (address.isBlank()) {
                                                validationError = "Waiting for automatic PC discovery. You can enter an address manually."
                                            } else {
                                                ContextCompat.startForegroundService(
                                                    this@MainActivity,
                                                    Intent(this@MainActivity, AudioPlaybackService::class.java)
                                                        .putExtra(AudioPlaybackService.EXTRA_HOST, address)
                                                )
                                                audioStarted = true
                                            }
                                        },
                                        enabled = !audioStarted
                                    ) {
                                        Text("Start audio")
                                    }
                                    Button(
                                        onClick = {
                                            stopService(Intent(this@MainActivity, AudioPlaybackService::class.java))
                                            audioStarted = false
                                        },
                                        enabled = audioStarted
                                    ) {
                                        Text("Stop audio")
                                    }
                                }
                            }
                        }
                        Text(
                            "Keep this screen focused while playing: Android delivers controller key and stick input " +
                                "to the foreground activity. Android may stop background services under power-saving policies."
                        )
                        Spacer(Modifier.height(8.dp))
                    }
                }
            }
        }
    }

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        if (event.action != KeyEvent.ACTION_DOWN && event.action != KeyEvent.ACTION_UP)
            return super.dispatchKeyEvent(event)

        val pressed = event.action == KeyEvent.ACTION_DOWN
        val code = event.keyCode
        var buttons = SharedControllerState.buttonsMask

        fun setBit(mask: UShort, bit: Int): UShort {
            val value = (1u shl bit).toUShort()
            return if (pressed) mask or value else mask and value.inv()
        }

        when (code) {
            KeyEvent.KEYCODE_BUTTON_A -> buttons = setBit(buttons, 0)
            KeyEvent.KEYCODE_BUTTON_B -> buttons = setBit(buttons, 1)
            KeyEvent.KEYCODE_BUTTON_X -> buttons = setBit(buttons, 2)
            KeyEvent.KEYCODE_BUTTON_Y -> buttons = setBit(buttons, 3)
            KeyEvent.KEYCODE_BUTTON_L1 -> buttons = setBit(buttons, 4)
            KeyEvent.KEYCODE_BUTTON_R1 -> buttons = setBit(buttons, 5)
            KeyEvent.KEYCODE_BUTTON_SELECT -> buttons = setBit(buttons, 6)
            KeyEvent.KEYCODE_BUTTON_START -> buttons = setBit(buttons, 7)
            KeyEvent.KEYCODE_BUTTON_THUMBL -> buttons = setBit(buttons, 8)
            KeyEvent.KEYCODE_BUTTON_THUMBR -> buttons = setBit(buttons, 9)
            KeyEvent.KEYCODE_BUTTON_MODE -> buttons = setBit(buttons, 10)
            KeyEvent.KEYCODE_DPAD_UP -> SharedControllerState.setDpadKey(0, pressed)
            KeyEvent.KEYCODE_DPAD_DOWN -> SharedControllerState.setDpadKey(1, pressed)
            KeyEvent.KEYCODE_DPAD_LEFT -> SharedControllerState.setDpadKey(2, pressed)
            KeyEvent.KEYCODE_DPAD_RIGHT -> SharedControllerState.setDpadKey(3, pressed)
        }

        SharedControllerState.buttonsMask = buttons
        SharedControllerState.markEvent()

        return when (code) {
            KeyEvent.KEYCODE_BUTTON_A, KeyEvent.KEYCODE_BUTTON_B,
            KeyEvent.KEYCODE_BUTTON_X, KeyEvent.KEYCODE_BUTTON_Y,
            KeyEvent.KEYCODE_BUTTON_L1, KeyEvent.KEYCODE_BUTTON_R1,
            KeyEvent.KEYCODE_BUTTON_SELECT, KeyEvent.KEYCODE_BUTTON_START,
            KeyEvent.KEYCODE_BUTTON_THUMBL, KeyEvent.KEYCODE_BUTTON_THUMBR,
            KeyEvent.KEYCODE_BUTTON_MODE, KeyEvent.KEYCODE_DPAD_UP,
            KeyEvent.KEYCODE_DPAD_DOWN, KeyEvent.KEYCODE_DPAD_LEFT,
            KeyEvent.KEYCODE_DPAD_RIGHT -> true
            else -> super.dispatchKeyEvent(event)
        }
    }

    override fun dispatchGenericMotionEvent(event: MotionEvent): Boolean {
        val isGameController =
            (event.source and InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD ||
                (event.source and InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK
        if (!isGameController) return super.dispatchGenericMotionEvent(event)

        fun axis(axis: Int): Float = event.getAxisValue(axis).coerceIn(-1f, 1f)
        fun trigger(axis: Int): Float = event.getAxisValue(axis).coerceIn(0f, 1f)

        SharedControllerState.leftX = axis(MotionEvent.AXIS_X)
        SharedControllerState.leftY = -axis(MotionEvent.AXIS_Y)
        SharedControllerState.rightX = axis(MotionEvent.AXIS_Z)
        SharedControllerState.rightY = -axis(MotionEvent.AXIS_RZ)
        SharedControllerState.leftTrigger = trigger(MotionEvent.AXIS_LTRIGGER)
            .takeIf { it > 0f } ?: trigger(MotionEvent.AXIS_BRAKE)
        SharedControllerState.rightTrigger = trigger(MotionEvent.AXIS_RTRIGGER)
            .takeIf { it > 0f } ?: trigger(MotionEvent.AXIS_GAS)
        SharedControllerState.setDpadHat(
            event.getAxisValue(MotionEvent.AXIS_HAT_X),
            event.getAxisValue(MotionEvent.AXIS_HAT_Y)
        )
        SharedControllerState.markEvent()
        return true
    }

    private fun isValidIpv4(value: String): Boolean {
        val parts = value.split('.')
        return parts.size == 4 && parts.all { part ->
            part.isNotEmpty() && part.all(Char::isDigit) &&
                part.toIntOrNull()?.let { it in 0..255 } == true
        }
    }
}
