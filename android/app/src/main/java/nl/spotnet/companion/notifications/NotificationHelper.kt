package nl.spotnet.companion.notifications

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.os.Build
import androidx.core.app.NotificationCompat
import nl.spotnet.companion.R
import nl.spotnet.companion.data.NotificationItem
import nl.spotnet.companion.data.PreferencesManager
import nl.spotnet.companion.ui.MainActivity

object NotificationHelper {
    const val CHANNEL_ID = "spotnet_alerts"

    fun createNotificationChannel(context: Context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val name = context.getString(R.string.notif_channel_name)
            val descriptionText = context.getString(R.string.notif_channel_desc)
            val importance = NotificationManager.IMPORTANCE_HIGH
            val channel = NotificationChannel(CHANNEL_ID, name, importance).apply {
                description = descriptionText
                enableLights(true)
                lightColor = Color.BLUE
                enableVibration(true)
                vibrationPattern = longArrayOf(0, 250, 150, 250)
            }
            val notificationManager =
                context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            notificationManager.createNotificationChannel(channel)
        }
    }

    fun showNotification(context: Context, item: NotificationItem) {
        createNotificationChannel(context)
        val prefs = PreferencesManager(context)

        val intent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK
            putExtra("EXTRA_NAV_TAB", "notifications")
            putExtra("EXTRA_NOTIF_ID", item.id)
        }

        val pendingIntent = PendingIntent.getActivity(
            context,
            item.id.hashCode(),
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val isDownload = item.ruleType.equals("Download", ignoreCase = true) || item.ruleId.equals("download", ignoreCase = true)
        val contentTitle = if (isDownload) {
            item.title.ifBlank { "Download voltooid" }
        } else {
            item.title.ifBlank { "Spotnet: Nieuwe spots voor ${item.ruleName}" }
        }

        // Build BigText with spot names
        val bigText = StringBuilder()
        bigText.append(item.body)
        if (item.spots.isNotEmpty()) {
            bigText.append("\n\n")
            item.spots.take(4).forEach { spot ->
                bigText.append("• ").append(spot.title)
                if (spot.formattedSize.isNotBlank()) {
                    bigText.append(" (").append(spot.formattedSize).append(")")
                }
                bigText.append("\n")
            }
            if (item.spots.size > 4) {
                bigText.append("... en nog ").append(item.spots.size - 4).append(" andere spot(s)")
            }
        }

        val builder = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_notifications)
            .setContentTitle(contentTitle)
            .setContentText(item.body)
            .setStyle(NotificationCompat.BigTextStyle().bigText(bigText.toString().trim()))
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setAutoCancel(true)
            .setContentIntent(pendingIntent)

        if (prefs.soundEnabled) {
            builder.setDefaults(NotificationCompat.DEFAULT_SOUND)
        }
        if (prefs.vibrationEnabled) {
            builder.setVibrate(longArrayOf(0, 250, 150, 250))
        }

        val notificationManager =
            context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        notificationManager.notify(item.id.hashCode(), builder.build())
    }
}
