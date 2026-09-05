package nl.spotnet.companion.notifications

import android.content.Context
import androidx.work.*
import nl.spotnet.companion.data.PreferencesManager
import nl.spotnet.companion.data.SpotnetApiClient
import java.util.concurrent.TimeUnit

class SpotnetNotificationWorker(
    private val context: Context,
    workerParams: WorkerParameters
) : CoroutineWorker(context, workerParams) {

    override suspend fun doWork(): Result {
        val prefs = PreferencesManager(context)
        if (!prefs.isConnected || !prefs.notificationsEnabled) {
            return Result.success()
        }

        val apiClient = SpotnetApiClient(context)
        val result = apiClient.getNotifications()

        if (result.isSuccess) {
            val response = result.getOrNull() ?: return Result.success()
            val shownIds = prefs.shownNotifIds

            for (notif in response.notifications) {
                // If it's unread and hasn't been notified on this phone yet
                if (!notif.isRead && !shownIds.contains(notif.id)) {
                    NotificationHelper.showNotification(context, notif)
                    prefs.markNotifAsShown(notif.id)
                }
            }
            return Result.success()
        }

        return Result.retry()
    }

    companion object {
        private const val WORK_NAME = "SpotnetPeriodicNotificationCheck"

        fun schedule(context: Context, intervalMinutes: Int = 15) {
            val constraints = Constraints.Builder()
                .setRequiredNetworkType(NetworkType.CONNECTED)
                .build()

            val actualInterval = intervalMinutes.coerceAtLeast(15) // WorkManager min is 15 minutes

            val workRequest = PeriodicWorkRequestBuilder<SpotnetNotificationWorker>(
                actualInterval.toLong(), TimeUnit.MINUTES
            )
                .setConstraints(constraints)
                .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 2, TimeUnit.MINUTES)
                .build()

            WorkManager.getInstance(context).enqueueUniquePeriodicWork(
                WORK_NAME,
                ExistingPeriodicWorkPolicy.UPDATE,
                workRequest
            )
        }

        fun runOnce(context: Context) {
            val workRequest = OneTimeWorkRequestBuilder<SpotnetNotificationWorker>()
                .setConstraints(
                    Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()
                )
                .build()

            WorkManager.getInstance(context).enqueue(workRequest)
        }

        fun cancel(context: Context) {
            WorkManager.getInstance(context).cancelUniqueWork(WORK_NAME)
        }
    }
}
