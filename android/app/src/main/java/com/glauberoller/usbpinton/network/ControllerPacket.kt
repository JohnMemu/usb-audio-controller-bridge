package com.glauberoller.usbpinton.network

import java.nio.ByteBuffer
import java.nio.ByteOrder

data class ControllerPacket(
    val seq: UInt,
    val lx: Float,
    val ly: Float,
    val rx: Float,
    val ry: Float,
    val lt: Float,
    val rt: Float,
    val buttonsMask: UShort,
    val dpadMask: UShort,
)

object ControllerPacketCodec {
    const val MAGIC: UInt = 0x43425553u // 'USBC' in little-endian

    fun toBytes(packet: ControllerPacket): ByteArray {
        val bb = ByteBuffer.allocate(40).order(ByteOrder.LITTLE_ENDIAN)
        bb.putInt(MAGIC.toInt())
        bb.putInt(packet.seq.toInt())
        bb.putFloat(packet.lx)
        bb.putFloat(packet.ly)
        bb.putFloat(packet.rx)
        bb.putFloat(packet.ry)
        bb.putFloat(packet.lt)
        bb.putFloat(packet.rt)
        bb.putShort(packet.buttonsMask.toShort())
        bb.putShort(packet.dpadMask.toShort())
        bb.putShort(0) // reserved
        return bb.array()
    }
}
