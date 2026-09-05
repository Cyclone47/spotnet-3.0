package nl.spotnet.companion

import android.app.Application
import nl.spotnet.companion.data.PreferencesManager
import nl.spotnet.companion.notifications.NotificationHelper
import nl.spotnet.companion.notifications.SpotnetNotificationWorker

class SpotnetApp : Application() {
    override fun onCreate() {
        super.onCreate()
        NotificationHelper.createNotificationChannel(this)

        val prefs = PreferencesManager(this)
        if (prefs.isConnected && prefs.notificationsEnabled) {
            SpotnetNotificationWorker.schedule(this, prefs.notificationIntervalMinutes)
        }
    }
}
