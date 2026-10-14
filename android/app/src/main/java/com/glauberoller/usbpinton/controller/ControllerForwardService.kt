package com.glauberoller.usbpinton.controller

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.IBinder
import android.os.SystemClock
import android.util.Log
import androidx.core.app.NotificationCompat
import com.glauberoller.usbpinton.BridgeStatus
import com.glauberoller.usbpinton.R
import com.glauberoller.usbpinton.network.ControllerPacket
import com.glauberoller.usbpinton.network.ControllerPacketBuilder
import com.glauberoller.usbpinton.network.ControllerPacketCodec
import com.glauberoller.usbpinton.vendorgamepad.GenericGamepadReader
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

class ControllerForwardService : Service() {
    private var workerThread: Thread? = null
    @Volatile private var running = false
    private val notificationId = 1337
    private val channelId = "controller_forward"

    override fun onCreate() {
        super.onCreate()
        createChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (running) return START_NOT_STICKY
        val host = intent?.getStringExtra(EXTRA_HOST)?.trim().orEmpty()
        if (host.isBlank()) {
            BridgeStatus.controller.value = "Controller service failed: PC address is missing."
            stopSelf(startId)
            return START_NOT_STICKY
        }

        startForeground(notificationId, buildNotification("Connecting to $host"))
        running = true
        BridgeStatus.controller.value = "Controller service starting for $host."
        workerThread = Thread({ runForwarder(host) }, "controller-forward").also { it.start() }
        return START_NOT_STICKY
    }

    private fun runForwarder(host: String) {
        try {
            val target = InetAddress.getByName(host)
            val gamepad = GenericGamepadReader(this)
            DatagramSocket().use { socket ->
                val bytesPerPacket = ByteArray(40)
                var lastStatusAt = 0L
                while (running) {
                    val startedAt = SystemClock.uptimeMillis()
                    if (gamepad.isControllerAvailable()) {
                        val packet: ControllerPacket = ControllerPacketBuilder.fromGamepad(gamepad)
                        val bytes = ControllerPacketCodec.toBytes(packet)
                        System.arraycopy(bytes, 0, bytesPerPacket, 0, bytes.size)
                        socket.send(DatagramPacket(bytesPerPacket, bytesPerPacket.size, target, CONTROLLER_PORT))
                        val now = SystemClock.uptimeMillis()
                        if (now - lastStatusAt >= 1000) {
                            lastStatusAt = now
                            val status = "Forwarding controller packets to $host:$CONTROLLER_PORT."
                            BridgeStatus.controller.value = status
                            updateNotification(status)
                        }
                    } else if (SystemClock.uptimeMillis() - lastStatusAt >= 1000) {
                        lastStatusAt = SystemClock.uptimeMillis()
                        val status = "Connected to $host; waiting for a gamepad input device."
                        BridgeStatus.controller.value = status
                        updateNotification(status)
                    }

                    val remaining = POLL_INTERVAL_MS - (SystemClock.uptimeMillis() - startedAt)
                    if (remaining > 0) SystemClock.sleep(remaining)
                }
            }
        } catch (exception: Exception) {
            if (running) {
                val message = "Controller forwarding failed: ${exception.message ?: exception.javaClass.simpleName}"
                Log.e(TAG, message, exception)
                BridgeStatus.controller.value = message
                updateNotification(message)
            }
        }
    }

    override fun onDestroy() {
        running = false
        workerThread?.interrupt()
        workerThread = null
        BridgeStatus.controller.value = "Controller service stopped."
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun createChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            manager.createNotificationChannel(
                NotificationChannel(channelId, "Controller forwarding", NotificationManager.IMPORTANCE_LOW)
            )
        }
    }

    private fun buildNotification(text: String): Notification =
        NotificationCompat.Builder(this, channelId)
            .setSmallIcon(android.R.drawable.ic_media_play)
            .setContentTitle("Controller Forwarder")
            .setContentText(text)
            .setOngoing(true)
            .build()

    private fun updateNotification(text: String) {
        val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        manager.notify(notificationId, buildNotification(text))
    }

    companion object {
        const val EXTRA_HOST = "com.glauberoller.usbpinton.controller.HOST"
        private const val CONTROLLER_PORT = 17050
        private const val POLL_INTERVAL_MS = 20L
        private const val TAG = "ControllerForwarder"
    }
}
