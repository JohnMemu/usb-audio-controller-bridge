package com.glauberoller.usbpinton.network

import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException

object PcDiscovery {
    private const val DISCOVERY_PORT = 17052
    private const val TIMEOUT_MS = 900
    private val request = "USBC-DISCOVER-1".toByteArray(Charsets.US_ASCII)
    private val response = "USBC-HOST-1".toByteArray(Charsets.US_ASCII)

    fun discover(): String? {
        DatagramSocket(null).use { socket ->
            socket.broadcast = true
            socket.soTimeout = TIMEOUT_MS
            socket.bind(InetSocketAddress(0))

            val broadcasts = findBroadcastAddresses().ifEmpty {
                listOf(InetAddress.getByName("255.255.255.255"))
            }
            for (broadcast in broadcasts) {
                socket.send(DatagramPacket(request, request.size, broadcast, DISCOVERY_PORT))
            }

            val buffer = ByteArray(64)
            val packet = DatagramPacket(buffer, buffer.size)
            return try {
                socket.receive(packet)
                if (packet.length == response.size &&
                    response.indices.all { buffer[it] == response[it] } &&
                    packet.address is Inet4Address
                ) packet.address.hostAddress else null
            } catch (_: SocketTimeoutException) {
                null
            }
        }
    }

    private fun findBroadcastAddresses(): List<InetAddress> =
        NetworkInterface.getNetworkInterfaces()?.toList()
            ?.filter { it.isUp && !it.isLoopback }
            ?.flatMap { network ->
                network.interfaceAddresses.mapNotNull { address ->
                    address.broadcast?.takeIf { address.address is Inet4Address }
                }
            }
            ?.distinctBy { it.hostAddress }
            .orEmpty()
}
