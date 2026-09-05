package nl.spotnet.companion.data

import org.json.JSONArray
import org.json.JSONObject

data class DiscoveredServer(
    val name: String,
    val host: String,
    val port: Int,
    val version: String,
    val machine: String,
    val requireAuth: Boolean
) {
    val baseUrl: String get() = "http://$host:$port"
}

data class ServerStatus(
    val version: String,
    val isReady: Boolean,
    val totalSpotsInDb: Long,
    val queueCount: Int,
    val downloadSpeedFormatted: String,
    val isSyncing: Boolean,
    val requireAuth: Boolean
) {
    companion object {
        fun fromJson(json: JSONObject): ServerStatus {
            return ServerStatus(
                version = json.optString("version", "3.0"),
                isReady = json.optBoolean("isReady", true),
                totalSpotsInDb = json.optLong("totalSpotsInDb", 0),
                queueCount = json.optInt("queueCount", 0),
                downloadSpeedFormatted = json.optString("downloadSpeedFormatted", "0 KB/s"),
                isSyncing = json.optBoolean("isSyncing", false),
                requireAuth = json.optBoolean("requireAuth", false)
            )
        }
    }
}

data class PairResponse(
    val success: Boolean,
    val deviceId: String?,
    val deviceToken: String?,
    val errorMessage: String?
) {
    companion object {
        fun fromJson(json: JSONObject): PairResponse {
            return PairResponse(
                success = json.optBoolean("success", false),
                deviceId = if (json.has("deviceId") && !json.isNull("deviceId")) json.optString("deviceId") else null,
                deviceToken = if (json.has("deviceToken") && !json.isNull("deviceToken")) json.optString("deviceToken") else null,
                errorMessage = if (json.has("errorMessage") && !json.isNull("errorMessage")) json.optString("errorMessage") else null
            )
        }
    }
}

data class NotificationSpot(
    val id: Long,
    val messageId: String,
    val title: String,
    val categoryName: String,
    val formattedSize: String,
    val formattedDate: String
) {
    companion object {
        fun fromJson(json: JSONObject): NotificationSpot {
            return NotificationSpot(
                id = json.optLong("id", 0),
                messageId = json.optString("messageId", ""),
                title = json.optString("title", ""),
                categoryName = json.optString("categoryName", ""),
                formattedSize = json.optString("formattedSize", ""),
                formattedDate = json.optString("formattedDate", "")
            )
        }
    }
}

data class NotificationItem(
    val id: String,
    val ruleId: String,
    val ruleName: String,
    val ruleType: String,
    val title: String,
    val body: String,
    val spotCount: Int,
    val timeAgo: String,
    val createdAtUtc: String,
    var isRead: Boolean,
    val spots: List<NotificationSpot>
) {
    companion object {
        fun fromJson(json: JSONObject): NotificationItem {
            val spotsArr = json.optJSONArray("spots") ?: JSONArray()
            val spotsList = mutableListOf<NotificationSpot>()
            for (i in 0 until spotsArr.length()) {
                val sObj = spotsArr.optJSONObject(i)
                if (sObj != null) {
                    spotsList.add(NotificationSpot.fromJson(sObj))
                }
            }
            return NotificationItem(
                id = json.optString("id", ""),
                ruleId = json.optString("ruleId", ""),
                ruleName = json.optString("ruleName", ""),
                ruleType = json.optString("ruleType", "Keyword"),
                title = json.optString("title", ""),
                body = json.optString("body", ""),
                spotCount = json.optInt("spotCount", 0),
                timeAgo = json.optString("timeAgo", ""),
                createdAtUtc = json.optString("createdAtUtc", ""),
                isRead = json.optBoolean("isRead", false),
                spots = spotsList
            )
        }
    }
}

data class NotificationsResponse(
    val unreadCount: Int,
    val notifications: List<NotificationItem>
) {
    companion object {
        fun fromJson(json: JSONObject): NotificationsResponse {
            val notifsArr = json.optJSONArray("notifications") ?: JSONArray()
            val notifsList = mutableListOf<NotificationItem>()
            for (i in 0 until notifsArr.length()) {
                val nObj = notifsArr.optJSONObject(i)
                if (nObj != null) {
                    notifsList.add(NotificationItem.fromJson(nObj))
                }
            }
            return NotificationsResponse(
                unreadCount = json.optInt("unreadCount", 0),
                notifications = notifsList
            )
        }
    }
}
