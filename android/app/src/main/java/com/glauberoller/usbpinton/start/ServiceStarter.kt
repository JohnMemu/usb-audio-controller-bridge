package com.glauberoller.usbpinton.start

import android.content.Context
import android.content.Intent
import androidx.core.content.ContextCompat
import com.glauberoller.usbpinton.controller.ControllerForwardService

fun Context.startControllerForwardService() {
    val intent = Intent(this, ControllerForwardService::class.java)
    ContextCompat.startForegroundService(this, intent)
}

fun Context.stopControllerForwardService() {
    val intent = Intent(this, ControllerForwardService::class.java)
    stopService(intent)
}
