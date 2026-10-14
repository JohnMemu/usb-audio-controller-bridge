package com.glauberoller.usbpinton.vendorgamepad

import android.content.Context
import android.hardware.input.InputManager
import android.view.InputDevice
import com.glauberoller.usbpinton.input.SharedControllerState

/**
 * Reads the latest controller state from SharedControllerState.
 * Controller availability is determined by presence of a gamepad-like InputDevice.
 */
class GenericGamepadReader(private val context: Context) {

    // Exposed values for packet builder
    var leftX: Float = 0f; private set
    var leftY: Float = 0f; private set
    var rightX: Float = 0f; private set
    var rightY: Float = 0f; private set
    var leftTrigger: Float = 0f; private set
    var rightTrigger: Float = 0f; private set
    var buttonsMask: UShort = 0u; private set
    var dpadMask: UShort = 0u; private set

    fun isControllerAvailable(): Boolean {
        val available = getGamepadDevices().isNotEmpty()
        if (!available) return false

        // Pull latest values from shared state
        leftX = SharedControllerState.leftX
        leftY = SharedControllerState.leftY
        rightX = SharedControllerState.rightX
        rightY = SharedControllerState.rightY
        leftTrigger = SharedControllerState.leftTrigger
        rightTrigger = SharedControllerState.rightTrigger
        buttonsMask = SharedControllerState.buttonsMask
        dpadMask = SharedControllerState.dpadMask
        return true
    }

    fun nextSeq(): UInt = SharedControllerState.nextSeq()

    private fun getGamepadDevices(): List<InputDevice> {
        val im = context.getSystemService(Context.INPUT_SERVICE) as InputManager
        val ids = im.inputDeviceIds
        val list = mutableListOf<InputDevice>()
        for (id in ids) {
            val dev = im.getInputDevice(id) ?: continue
            val sources = dev.sources
            val isGamepad = (sources and InputDevice.SOURCE_GAMEPAD) != 0 ||
                (sources and InputDevice.SOURCE_JOYSTICK) != 0
            if (isGamepad) list.add(dev)
        }
        return list
    }
}
