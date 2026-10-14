package com.glauberoller.usbpinton.audio

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioTrack
import android.os.Build
import android.os.IBinder
import android.os.Process
import android.util.Log
import androidx.core.app.NotificationCompat
import com.glauberoller.usbpinton.BridgeStatus
import java.io.DataInputStream
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder

class AudioPlaybackService : Service() {
    @Volatile private var running = false
    @Volatile private var socket: Socket? = null
    @Volatile private var audioTrack: AudioTrack? = null
    private var workerThread: Thread? = null
    private val notificationId = 1338
    private val channelId = "pc_audio"

    override fun onCreate() {
        super.onCreate()
        createChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (running) return START_NOT_STICKY
        val host = intent?.getStringExtra(EXTRA_HOST)?.trim().orEmpty()
        if (host.isBlank()) {
            BridgeStatus.audio.value = "Audio service failed: PC address is missing."
            stopSelf(startId)
            return START_NOT_STICKY
        }

        startForeground(notificationId, buildNotification("Connecting to $host:17051"))
        running = true
        BridgeStatus.audio.value = "Connecting to $host:17051."
        workerThread = Thread({ playStream(host) }, "pc-audio-playback").also { it.start() }
        return START_NOT_STICKY
    }

    private fun playStream(host: String) {
        try {
            Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO)
            val connection = Socket()
            socket = connection
            connection.tcpNoDelay = true
            connection.receiveBufferSize = 4096
            connection.connect(InetSocketAddress(host, AUDIO_PORT), CONNECT_TIMEOUT_MS)
            val input = DataInputStream(connection.getInputStream())
            val header = ByteArray(HEADER_SIZE)
            input.readFully(header)
            if (!header.copyOfRange(0, 4).contentEquals(byteArrayOf(0x41, 0x55, 0x44, 0x31)))
                throw IOException("PC sent an unrecognized audio stream header.")

            val metadata = ByteBuffer.wrap(header).order(ByteOrder.LITTLE_ENDIAN)
            metadata.position(4)
            val sampleRate = metadata.int
            val channels = metadata.short.toInt() and 0xffff
            val encoding = metadata.short.toInt() and 0xffff
            if (sampleRate !in 8_000..192_000 || channels !in 1..2 || encoding != PCM16_ENCODING)
                throw IOException("Unsupported PC audio format ($sampleRate Hz, $channels channels, encoding $encoding).")

            val channelMask = if (channels == 1) AudioFormat.CHANNEL_OUT_MONO else AudioFormat.CHANNEL_OUT_STEREO
            val minimumBuffer = AudioTrack.getMinBufferSize(
                sampleRate,
                channelMask,
                AudioFormat.ENCODING_PCM_16BIT
            )
            if (minimumBuffer <= 0) throw IOException("Android does not support this audio output format.")
            val track = AudioTrack.Builder()
                .setAudioAttributes(
                    AudioAttributes.Builder()
                        .setUsage(AudioAttributes.USAGE_MEDIA)
                        .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                        .build()
                )
                .setAudioFormat(
                    AudioFormat.Builder()
                        .setSampleRate(sampleRate)
                        .setChannelMask(channelMask)
                        .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                        .build()
                )
                .setBufferSizeInBytes(minimumBuffer)
                .setTransferMode(AudioTrack.MODE_STREAM)
                .setPerformanceMode(AudioTrack.PERFORMANCE_MODE_LOW_LATENCY)
                .build()
            audioTrack = track
            track.play()
            val status = "Playing PC audio ($sampleRate Hz, $channels channel(s))."
            BridgeStatus.audio.value = status
            updateNotification(status)

            val frameBytes = channels * 2
            val buffer = ByteArray(1024 + frameBytes)
            var pending = 0
            while (running) {
                val read = input.read(buffer, pending, buffer.size - pending)
                if (read < 0) throw IOException("PC audio stream ended.")
                val total = pending + read
                val playable = total - total % frameBytes
                var offset = 0
                while (offset < playable && running) {
                    val written = track.write(buffer, offset, playable - offset, AudioTrack.WRITE_BLOCKING)
                    if (written < 0) throw IOException("Android audio output failed ($written).")
                    if (written == 0) throw IOException("Android audio output stopped accepting samples.")
                    offset += written
                }
                pending = total - playable
                if (pending > 0) System.arraycopy(buffer, playable, buffer, 0, pending)
            }
        } catch (exception: Exception) {
            if (running) {
                val message = "Audio playback failed: ${exception.message ?: exception.javaClass.simpleName}"
                Log.e(TAG, message, exception)
                BridgeStatus.audio.value = message
                updateNotification(message)
            }
        } finally {
            audioTrack?.let { track ->
                if (track.playState == AudioTrack.PLAYSTATE_PLAYING) track.stop()
                track.release()
            }
            audioTrack = null
            socket?.close()
            socket = null
        }
    }

    override fun onDestroy() {
        running = false
        socket?.close()
        audioTrack?.pause()
        workerThread?.interrupt()
        workerThread = null
        BridgeStatus.audio.value = "Audio service stopped."
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun createChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            manager.createNotificationChannel(
                NotificationChannel(channelId, "PC audio playback", NotificationManager.IMPORTANCE_LOW)
            )
        }
    }

    private fun buildNotification(text: String): Notification =
        NotificationCompat.Builder(this, channelId)
            .setSmallIcon(android.R.drawable.ic_media_play)
            .setContentTitle("PC Audio")
            .setContentText(text)
            .setOngoing(true)
            .build()

    private fun updateNotification(text: String) {
        val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        manager.notify(notificationId, buildNotification(text))
    }

    companion object {
        const val EXTRA_HOST = "com.glauberoller.usbpinton.audio.HOST"
        private const val AUDIO_PORT = 17051
        private const val HEADER_SIZE = 16
        private const val PCM16_ENCODING = 1
        private const val CONNECT_TIMEOUT_MS = 5_000
        private const val TAG = "AudioPlaybackService"
    }
}
