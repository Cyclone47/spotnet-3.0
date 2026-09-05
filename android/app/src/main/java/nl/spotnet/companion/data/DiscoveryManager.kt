package nl.spotnet.companion.data

import android.content.Context
import android.net.wifi.WifiManager
import kotlinx.coroutines.*
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket

class DiscoveryManager(private val context: Context) {

    private var isScanning = false
    private var scanJob: Job? = null

    fun startDiscovery(
        scope: CoroutineScope,
        onServerFound: (DiscoveredServer) -> Unit,
        onScanFinished: () -> Unit
    ) {
        if (isScanning) return
        isScanning = true

        val foundHosts = mutableSetOf<String>()

        fun emitIfNew(server: DiscoveredServer) {
            val key = "${server.host}:${server.port}"
            synchronized(foundHosts) {
                if (foundHosts.add(key)) {
                    scope.launch(Dispatchers.Main) {
                        onServerFound(server)
                    }
                }
            }
        }

        scanJob = scope.launch(Dispatchers.IO) {
            // 1. Acquire WiFi Multicast lock
            var multicastLock: WifiManager.MulticastLock? = null
            try {
                val wifiManager = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
                multicastLock = wifiManager?.createMulticastLock("SpotnetDiscoveryLock")
                multicastLock?.setReferenceCounted(true)
                multicastLock?.acquire()
            } catch (_: Exception) {}

            // 2. Start UDP listener & sender
            val udpJob = launch {
                listenAndSendUdp(::emitIfNew)
            }

            // 3. Parallel subnet scan as fallback
            val subnetJob = launch {
                scanSubnet(::emitIfNew)
            }

            // Let scanning run for 5 seconds
            delay(5000)

            try {
                multicastLock?.let {
                    if (it.isHeld) it.release()
                }
            } catch (_: Exception) {}

            udpJob.cancel()
            subnetJob.cancel()
            isScanning = false

            withContext(Dispatchers.Main) {
                onScanFinished()
            }
        }
    }

    fun stopDiscovery() {
        scanJob?.cancel()
        scanJob = null
        isScanning = false
    }

    private suspend fun listenAndSendUdp(onFound: (DiscoveredServer) -> Unit) = withContext(Dispatchers.IO) {
        var socket: DatagramSocket? = null
        try {
            socket = DatagramSocket(null).apply {
                reuseAddress = true
                broadcast = true
                soTimeout = 4000
                bind(InetSocketAddress(8771))
            }

            // Send discovery pings to broadcast
            launch {
                val pingData = "SPOTNET_DISCOVER".toByteArray(Charsets.UTF_8)
                val broadcastAddresses = getBroadcastAddresses()
                for (repeat in 0 until 3) {
                    for (addr in broadcastAddresses) {
                        try {
                            val packet = DatagramPacket(pingData, pingData.size, addr, 8771)
                            socket.send(packet)
                        } catch (_: Exception) {}
                    }
                    delay(800)
                }
            }

            // Receive loop
            val buffer = ByteArray(2048)
            while (isActive) {
                try {
                    val packet = DatagramPacket(buffer, buffer.size)
                    socket.receive(packet)
                    val senderIp = packet.address.hostAddress ?: continue
                    val text = String(packet.data, 0, packet.length, Charsets.UTF_8).trim()

                    val jsonStr = when {
                        text.startsWith("SPOTNET_DISCOVERY_PONG:") -> text.removePrefix("SPOTNET_DISCOVERY_PONG:")
                        text.startsWith("SPOTNET_BEACON:") -> text.removePrefix("SPOTNET_BEACON:")
                        text.startsWith("{") -> text
                        else -> null
                    }

                    if (jsonStr != null) {
                        parseServerJson(jsonStr, senderIp)?.let(onFound)
                    }
                } catch (_: Exception) {
                    if (!isActive) break
                }
            }
        } catch (_: Exception) {
        } finally {
            socket?.close()
        }
    }

    private suspend fun scanSubnet(onFound: (DiscoveredServer) -> Unit) = withContext(Dispatchers.IO) {
        val localIp = getLocalIpAddress() ?: return@withContext
        val parts = localIp.split(".")
        if (parts.size != 4) return@withContext
        val prefix = "${parts[0]}.${parts[1]}.${parts[2]}"

        // Probe 1..254 concurrently in batches
        val client = okhttp3.OkHttpClient.Builder()
            .connectTimeout(300, java.util.concurrent.TimeUnit.MILLISECONDS)
            .readTimeout(500, java.util.concurrent.TimeUnit.MILLISECONDS)
            .build()

        coroutineScope {
            for (i in 1..254) {
                if (!isActive) break
                launch {
                    val targetIp = "$prefix.$i"
                    try {
                        val request = okhttp3.Request.Builder()
                            .url("http://$targetIp:8770/api/v1/status")
                            .build()
                        val response = client.newCall(request).execute()
                        if (response.isSuccessful) {
                            val body = response.body?.string() ?: ""
                            val json = JSONObject(body)
                            val server = DiscoveredServer(
                                name = "Spotnet Desktop",
                                host = targetIp,
                                port = 8770,
                                version = json.optString("version", "3.0"),
                                machine = targetIp,
                                requireAuth = json.optBoolean("requireAuth", false)
                            )
                            onFound(server)
                        }
                    } catch (_: Exception) {}
                }
            }
        }
    }

    private fun parseServerJson(jsonStr: String, senderIp: String): DiscoveredServer? {
        return try {
            val json = JSONObject(jsonStr)
            if (json.optString("service") == "spotnet-remote" || json.has("version")) {
                DiscoveredServer(
                    name = json.optString("name", "Spotnet Desktop"),
                    host = senderIp,
                    port = json.optInt("port", 8770),
                    version = json.optString("version", "3.0"),
                    machine = json.optString("machine", senderIp),
                    requireAuth = json.optBoolean("requireAuth", false)
                )
            } else null
        } catch (_: Exception) {
            null
        }
    }

    private fun getBroadcastAddresses(): List<InetAddress> {
        val list = mutableListOf<InetAddress>()
        try {
            list.add(InetAddress.getByName("255.255.255.255"))
            val interfaces = java.net.NetworkInterface.getNetworkInterfaces()
            while (interfaces.hasMoreElements()) {
                val iface = interfaces.nextElement()
                if (iface.isLoopback || !iface.isUp) continue
                for (addr in iface.interfaceAddresses) {
                    val bcast = addr.broadcast
                    if (bcast != null) {
                        list.add(bcast)
                    }
                }
            }
        } catch (_: Exception) {}
        return list
    }

    private fun getLocalIpAddress(): String? {
        try {
            val interfaces = java.net.NetworkInterface.getNetworkInterfaces()
            while (interfaces.hasMoreElements()) {
                val iface = interfaces.nextElement()
                if (iface.isLoopback || !iface.isUp) continue
                val addresses = iface.inetAddresses
                while (addresses.hasMoreElements()) {
                    val addr = addresses.nextElement()
                    if (!addr.isLoopbackAddress && addr is java.net.Inet4Address) {
                        return addr.hostAddress
                    }
                }
            }
        } catch (_: Exception) {}
        return null
    }
}
