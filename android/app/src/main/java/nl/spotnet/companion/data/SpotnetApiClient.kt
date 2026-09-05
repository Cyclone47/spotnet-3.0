package nl.spotnet.companion.data

import android.content.Context
import android.os.Build
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.util.concurrent.TimeUnit

class SpotnetApiClient(context: Context) {
    private val prefs = PreferencesManager(context)

    private val client = OkHttpClient.Builder()
        .connectTimeout(5, TimeUnit.SECONDS)
        .readTimeout(10, TimeUnit.SECONDS)
        .writeTimeout(10, TimeUnit.SECONDS)
        .build()

    private val jsonMediaType = "application/json; charset=utf-8".toMediaType()

    suspend fun getStatus(host: String = prefs.serverHost, port: Int = prefs.serverPort): Result<ServerStatus> =
        withContext(Dispatchers.IO) {
            try {
                val url = "http://$host:$port/api/v1/status"
                val request = Request.Builder().url(url).get().build()
                val response = client.newCall(request).execute()
                if (response.isSuccessful) {
                    val body = response.body?.string() ?: ""
                    val json = JSONObject(body)
                    Result.success(ServerStatus.fromJson(json))
                } else {
                    Result.failure(Exception("HTTP ${response.code}: ${response.message}"))
                }
            } catch (e: Exception) {
                Result.failure(e)
            }
        }

    suspend fun pairWithPin(host: String, port: Int, pin: String): Result<PairResponse> =
        withContext(Dispatchers.IO) {
            pairInternal(host, port, pin = pin, token = null)
        }

    suspend fun pairWithToken(host: String, port: Int, token: String): Result<PairResponse> =
        withContext(Dispatchers.IO) {
            pairInternal(host, port, pin = null, token = token)
        }

    suspend fun loginWithCredentials(host: String, port: Int, username: String, password: String): Result<PairResponse> =
        withContext(Dispatchers.IO) {
            try {
                val url = "http://$host:$port/api/v1/auth/login"
                val deviceName = "Android (${Build.MANUFACTURER.replaceFirstChar { it.uppercase() }} ${Build.MODEL})"

                val jsonBody = JSONObject().apply {
                    put("username", username.trim())
                    put("password", password)
                    put("deviceName", deviceName)
                }

                val request = Request.Builder()
                    .url(url)
                    .post(jsonBody.toString().toRequestBody(jsonMediaType))
                    .build()

                val response = client.newCall(request).execute()
                val body = response.body?.string() ?: ""
                val json = JSONObject(body)
                val loginRes = PairResponse.fromJson(json)

                if (loginRes.success && !loginRes.deviceToken.isNullOrBlank()) {
                    prefs.serverHost = host
                    prefs.serverPort = port
                    prefs.deviceToken = loginRes.deviceToken
                    prefs.deviceId = loginRes.deviceId ?: ""
                    Result.success(loginRes)
                } else {
                    Result.failure(Exception(loginRes.errorMessage ?: "Inloggen mislukt. Controleer gebruikersnaam en wachtwoord."))
                }
            } catch (e: Exception) {
                Result.failure(e)
            }
        }

    private fun pairInternal(host: String, port: Int, pin: String?, token: String?): Result<PairResponse> {
        try {
            val url = "http://$host:$port/api/v1/auth/pair"
            val deviceName = "Android (${Build.MANUFACTURER.replaceFirstChar { it.uppercase() }} ${Build.MODEL})"

            val jsonBody = JSONObject().apply {
                if (!pin.isNullOrBlank()) put("pin", pin.trim())
                if (!token.isNullOrBlank()) put("token", token.trim())
                put("deviceName", deviceName)
            }

            val request = Request.Builder()
                .url(url)
                .post(jsonBody.toString().toRequestBody(jsonMediaType))
                .build()

            val response = client.newCall(request).execute()
            val body = response.body?.string() ?: ""
            val json = JSONObject(body)
            val pairRes = PairResponse.fromJson(json)

            if (pairRes.success && !pairRes.deviceToken.isNullOrBlank()) {
                prefs.serverHost = host
                prefs.serverPort = port
                prefs.deviceToken = pairRes.deviceToken
                prefs.deviceId = pairRes.deviceId ?: ""
                return Result.success(pairRes)
            } else {
                return Result.failure(Exception(pairRes.errorMessage ?: "Koppelen mislukt."))
            }
        } catch (e: Exception) {
            return Result.failure(e)
        }
    }

    suspend fun getNotifications(): Result<NotificationsResponse> = withContext(Dispatchers.IO) {
        try {
            val url = "${prefs.baseUrl}/api/v1/notifications"
            val reqBuilder = Request.Builder().url(url).get()
            if (prefs.deviceToken.isNotBlank()) {
                reqBuilder.addHeader("Authorization", "Bearer ${prefs.deviceToken}")
            }
            val response = client.newCall(reqBuilder.build()).execute()
            if (response.isSuccessful) {
                val body = response.body?.string() ?: ""
                val json = JSONObject(body)
                Result.success(NotificationsResponse.fromJson(json))
            } else {
                Result.failure(Exception("HTTP ${response.code}"))
            }
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun markNotificationRead(id: String): Result<Boolean> = withContext(Dispatchers.IO) {
        try {
            val url = "${prefs.baseUrl}/api/v1/notifications/$id/read"
            val reqBuilder = Request.Builder()
                .url(url)
                .post("{}".toRequestBody(jsonMediaType))
            if (prefs.deviceToken.isNotBlank()) {
                reqBuilder.addHeader("Authorization", "Bearer ${prefs.deviceToken}")
            }
            val response = client.newCall(reqBuilder.build()).execute()
            Result.success(response.isSuccessful)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun markAllNotificationsRead(): Result<Boolean> = withContext(Dispatchers.IO) {
        try {
            val url = "${prefs.baseUrl}/api/v1/notifications/read-all"
            val reqBuilder = Request.Builder()
                .url(url)
                .post("{}".toRequestBody(jsonMediaType))
            if (prefs.deviceToken.isNotBlank()) {
                reqBuilder.addHeader("Authorization", "Bearer ${prefs.deviceToken}")
            }
            val response = client.newCall(reqBuilder.build()).execute()
            Result.success(response.isSuccessful)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun deleteNotification(id: String): Result<Boolean> = withContext(Dispatchers.IO) {
        try {
            val url = "${prefs.baseUrl}/api/v1/notifications/$id"
            val reqBuilder = Request.Builder()
                .url(url)
                .delete()
            if (prefs.deviceToken.isNotBlank()) {
                reqBuilder.addHeader("Authorization", "Bearer ${prefs.deviceToken}")
            }
            val response = client.newCall(reqBuilder.build()).execute()
            Result.success(response.isSuccessful)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun triggerSpotsSync(): Result<Boolean> = withContext(Dispatchers.IO) {
        try {
            val url = "${prefs.baseUrl}/api/v1/spots/sync"
            val reqBuilder = Request.Builder()
                .url(url)
                .post("{}".toRequestBody(jsonMediaType))
            if (prefs.deviceToken.isNotBlank()) {
                reqBuilder.addHeader("Authorization", "Bearer ${prefs.deviceToken}")
            }
            val response = client.newCall(reqBuilder.build()).execute()
            Result.success(response.isSuccessful)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }
}
