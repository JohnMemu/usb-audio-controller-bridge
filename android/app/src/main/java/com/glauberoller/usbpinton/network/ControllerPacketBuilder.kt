package com.glauberoller.usbpinton.network

import com.glauberoller.usbpinton.vendorgamepad.GenericGamepadReader

object ControllerPacketBuilder {

    fun fromGamepad(reader: GenericGamepadReader): ControllerPacket {
        val lx = reader.leftX
        val ly = reader.leftY
        val rx = reader.rightX
        val ry = reader.rightY
        val lt = reader.leftTrigger
        val rt = reader.rightTrigger

        val buttonsMask = reader.buttonsMask
        val dpadMask = reader.dpadMask

        return ControllerPacket(
            seq = reader.nextSeq(),
            lx = lx,
            ly = ly,
            rx = rx,
            ry = ry,
            lt = lt,
            rt = rt,
            buttonsMask = buttonsMask,
            dpadMask = dpadMask,
        )
    }
}
