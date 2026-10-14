package com.glauberoller.usbpinton.input

import android.view.KeyEvent
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicLong

/**
 * Thread-safe shared state updated by MainActivity input callbacks,
 * read by the foreground service.
 */
object SharedControllerState {

    private val seq = AtomicInteger(1)

    // Axes normalized
    @Volatile var leftX: Float = 0f
    @Volatile var leftY: Float = 0f
    @Volatile var rightX: Float = 0f
    @Volatile var rightY: Float = 0f
    @Volatile var leftTrigger: Float = 0f
    @Volatile var rightTrigger: Float = 0f

    // Buttons
    @Volatile var buttonsMask: UShort = 0u
    @Volatile private var dpadKeyMask: UShort = 0u
    @Volatile private var dpadHatMask: UShort = 0u
    val dpadMask: UShort
        get() = dpadKeyMask or dpadHatMask

    @Volatile var lastEventUptimeMs: Long = 0L

    fun nextSeq(): UInt = seq.getAndIncrement().toUInt()

    fun markEvent() {
        lastEventUptimeMs = android.os.SystemClock.uptimeMillis()
    }

    fun setDpadKey(bit: Int, pressed: Boolean) {
        dpadKeyMask = setBit(dpadKeyMask, bit, pressed)
    }

    fun setDpadHat(x: Float, y: Float) {
        var mask = 0u.toUShort()
        if (y < -0.5f) mask = setBit(mask, 0, true)
        if (y > 0.5f) mask = setBit(mask, 1, true)
        if (x < -0.5f) mask = setBit(mask, 2, true)
        if (x > 0.5f) mask = setBit(mask, 3, true)
        dpadHatMask = mask
    }

    private fun setBit(mask: UShort, bit: Int, pressed: Boolean): UShort {
        val value = (1u shl bit).toUShort()
        return if (pressed) mask or value else mask and value.inv()
    }
}
