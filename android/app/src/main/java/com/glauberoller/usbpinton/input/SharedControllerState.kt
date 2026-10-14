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
    @Volatile var dpadMask: UShort = 0u

    @Volatile var lastEventUptimeMs: Long = 0L

    fun nextSeq(): UInt = seq.getAndIncrement().toUInt()

    fun markEvent() {
        lastEventUptimeMs = android.os.SystemClock.uptimeMillis()
    }
}
