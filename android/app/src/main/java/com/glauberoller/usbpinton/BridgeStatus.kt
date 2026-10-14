package com.glauberoller.usbpinton

import kotlinx.coroutines.flow.MutableStateFlow

object BridgeStatus {
    val controller = MutableStateFlow("Controller service stopped.")
    val audio = MutableStateFlow("Audio service stopped.")
}
