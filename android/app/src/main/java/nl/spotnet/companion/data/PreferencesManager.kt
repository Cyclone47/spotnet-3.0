package nl.spotnet.companion.data

import android.content.Context
import android.content.SharedPreferences

class PreferencesManager(context: Context) {
    private val prefs: SharedPreferences =
        context.getSharedPreferences("spotnet_companion_prefs", Context.MODE_PRIVATE)

    companion object {
        private const val KEY_SERVER_HOST = "server_host"
        private const val KEY_SERVER_PORT = "server_port"
        private const val KEY_DEVICE_TOKEN = "device_token"
        private const val KEY_DEVICE_ID = "device_id"
        private const val KEY_SERVER_NAME = "server_name"
        private const val KEY_NOTIF_ENABLED = "notifications_enabled"
        private const val KEY_NOTIF_INTERVAL = "notifications_interval"
        private const val KEY_NOTIF_SOUND = "notifications_sound"
        private const val KEY_NOTIF_VIBRATE = "notifications_vibrate"
        private const val KEY_LAST_NOTIF_ID = "last_notif_id"
        private const val KEY_SHOWN_NOTIF_IDS = "shown_notif_ids"
    }

    var serverHost: String
        get() = prefs.getString(KEY_SERVER_HOST, "") ?: ""
        set(value) = prefs.edit().putString(KEY_SERVER_HOST, value.trim()).apply()

    var serverPort: Int
        get() = prefs.getInt(KEY_SERVER_PORT, 8770)
        set(value) = prefs.edit().putInt(KEY_SERVER_PORT, value).apply()

    var deviceToken: String
        get() = prefs.getString(KEY_DEVICE_TOKEN, "") ?: ""
        set(value) = prefs.edit().putString(KEY_DEVICE_TOKEN, value).apply()

    var deviceId: String
        get() = prefs.getString(KEY_DEVICE_ID, "") ?: ""
        set(value) = prefs.edit().putString(KEY_DEVICE_ID, value).apply()

    var serverName: String
        get() = prefs.getString(KEY_SERVER_NAME, "Spotnet Desktop") ?: "Spotnet Desktop"
        set(value) = prefs.edit().putString(KEY_SERVER_NAME, value).apply()

    var notificationsEnabled: Boolean
        get() = prefs.getBoolean(KEY_NOTIF_ENABLED, true)
        set(value) = prefs.edit().putBoolean(KEY_NOTIF_ENABLED, value).apply()

    var notificationIntervalMinutes: Int
        get() = prefs.getInt(KEY_NOTIF_INTERVAL, 15)
        set(value) = prefs.edit().putInt(KEY_NOTIF_INTERVAL, value).apply()

    var soundEnabled: Boolean
        get() = prefs.getBoolean(KEY_NOTIF_SOUND, true)
        set(value) = prefs.edit().putBoolean(KEY_NOTIF_SOUND, value).apply()

    var vibrationEnabled: Boolean
        get() = prefs.getBoolean(KEY_NOTIF_VIBRATE, true)
        set(value) = prefs.edit().putBoolean(KEY_NOTIF_VIBRATE, value).apply()

    var lastNotifId: String
        get() = prefs.getString(KEY_LAST_NOTIF_ID, "") ?: ""
        set(value) = prefs.edit().putString(KEY_LAST_NOTIF_ID, value).apply()

    var shownNotifIds: Set<String>
        get() = prefs.getStringSet(KEY_SHOWN_NOTIF_IDS, emptySet()) ?: emptySet()
        set(value) = prefs.edit().putStringSet(KEY_SHOWN_NOTIF_IDS, value).apply()

    fun markNotifAsShown(id: String) {
        val current = shownNotifIds.toMutableSet()
        current.add(id)
        shownNotifIds = current
    }

    val isConnected: Boolean
        get() = serverHost.isNotBlank()

    val baseUrl: String
        get() = "http://$serverHost:$serverPort"

    fun disconnect() {
        prefs.edit()
            .remove(KEY_SERVER_HOST)
            .remove(KEY_SERVER_PORT)
            .remove(KEY_DEVICE_TOKEN)
            .remove(KEY_DEVICE_ID)
            .remove(KEY_SERVER_NAME)
            .apply()
    }
}
