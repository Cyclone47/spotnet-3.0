package nl.spotnet.companion.ui

import android.content.DialogInterface
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.view.View
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.updatePadding
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.google.android.material.tabs.TabLayout
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.launch
import nl.spotnet.companion.R
import nl.spotnet.companion.data.*
import nl.spotnet.companion.databinding.ActivityConnectBinding
import nl.spotnet.companion.notifications.SpotnetNotificationWorker

class ConnectActivity : AppCompatActivity() {

    private lateinit var binding: ActivityConnectBinding
    private lateinit var prefs: PreferencesManager
    private lateinit var discoveryManager: DiscoveryManager
    private lateinit var serversAdapter: DiscoveredServersAdapter
    private lateinit var apiClient: SpotnetApiClient

    private val qrScannerLauncher = registerForActivityResult(ScanContract()) { result ->
        if (result.contents != null) {
            handleScannedQr(result.contents)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityConnectBinding.inflate(layoutInflater)
        setContentView(binding.root)

        // Apply window insets for status bar (notch / cutout) and navigation bar
        ViewCompat.setOnApplyWindowInsetsListener(binding.appBarLayout) { v, insets ->
            val statusBars = insets.getInsets(WindowInsetsCompat.Type.statusBars())
            v.updatePadding(top = statusBars.top)
            insets
        }
        ViewCompat.setOnApplyWindowInsetsListener(binding.rootConnect) { _, insets ->
            val navBars = insets.getInsets(WindowInsetsCompat.Type.navigationBars())
            binding.rootConnect.updatePadding(bottom = navBars.bottom)
            insets
        }

        prefs = PreferencesManager(this)
        discoveryManager = DiscoveryManager(this)
        apiClient = SpotnetApiClient(this)

        // If already paired and connected, go directly to MainActivity
        if (prefs.isConnected && intent.getBooleanExtra("EXTRA_FORCE_CONNECT", false).not()) {
            startActivity(Intent(this, MainActivity::class.java))
            finish()
            return
        }

        setupTabs()
        setupDiscoveryList()
        setupPinTab()
        setupQrTab()

        // Start scanning automatically on open
        startScanning()
    }

    private fun setupTabs() {
        binding.tabLayout.addOnTabSelectedListener(object : TabLayout.OnTabSelectedListener {
            override fun onTabSelected(tab: TabLayout.Tab?) {
                when (tab?.position) {
                    0 -> {
                        binding.viewDiscovery.visibility = View.VISIBLE
                        binding.viewQr.visibility = View.GONE
                        binding.viewPin.visibility = View.GONE
                        startScanning()
                    }
                    1 -> {
                        binding.viewDiscovery.visibility = View.GONE
                        binding.viewQr.visibility = View.VISIBLE
                        binding.viewPin.visibility = View.GONE
                        discoveryManager.stopDiscovery()
                    }
                    2 -> {
                        binding.viewDiscovery.visibility = View.GONE
                        binding.viewQr.visibility = View.GONE
                        binding.viewPin.visibility = View.VISIBLE
                        discoveryManager.stopDiscovery()
                    }
                }
            }
            override fun onTabUnselected(tab: TabLayout.Tab?) {}
            override fun onTabReselected(tab: TabLayout.Tab?) {}
        })
    }

    private fun setupDiscoveryList() {
        serversAdapter = DiscoveredServersAdapter { server ->
            connectToDiscoveredServer(server)
        }
        binding.rvDiscoveredServers.layoutManager = LinearLayoutManager(this)
        binding.rvDiscoveredServers.adapter = serversAdapter

        binding.btnRescan.setOnClickListener {
            startScanning()
        }
    }

    private fun startScanning() {
        binding.progressScan.visibility = View.VISIBLE
        binding.tvScanStatus.text = "Bezig met zoeken naar Spotnet op het netwerk…"
        binding.layoutNoServers.visibility = View.GONE

        discoveryManager.startDiscovery(
            lifecycleScope,
            onServerFound = { server ->
                serversAdapter.addServer(server)
                binding.layoutNoServers.visibility = View.GONE
                binding.tvScanStatus.text = "Spotnet client(s) gevonden!"
            },
            onScanFinished = {
                binding.progressScan.visibility = View.GONE
                if (serversAdapter.itemCount == 0) {
                    binding.layoutNoServers.visibility = View.VISIBLE
                    binding.tvScanStatus.text = "Geen Spotnet client gevonden op het netwerk."
                } else {
                    binding.tvScanStatus.text = "${serversAdapter.itemCount} Spotnet client(s) gevonden op netwerk."
                }
            }
        )
    }

    private fun connectToDiscoveredServer(server: DiscoveredServer) {
        prefs.serverHost = server.host
        prefs.serverPort = server.port
        prefs.serverName = server.name

        if (server.requireAuth) {
            showLoginDialog(server)
        } else {
            onConnectedSuccess()
        }
    }

    private fun showLoginDialog(server: DiscoveredServer) {
        val dialogView = layoutInflater.inflate(R.layout.dialog_login, null)
        val etUser = dialogView.findViewById<com.google.android.material.textfield.TextInputEditText>(R.id.etUsername)
        val etPass = dialogView.findViewById<com.google.android.material.textfield.TextInputEditText>(R.id.etPassword)
        val progress = dialogView.findViewById<View>(R.id.progressLogin)

        val dialog = com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
            .setTitle("Inloggen op ${server.name}")
            .setView(dialogView)
            .setPositiveButton("Inloggen", null as DialogInterface.OnClickListener?)
            .setNeutralButton("Koppelcode gebruiken") { _, _ ->
                binding.etHost.setText(server.host)
                binding.etPort.setText(server.port.toString())
                binding.tabLayout.getTabAt(2)?.select()
                binding.toggleAuthMode.check(R.id.btnAuthPin)
            }
            .setNegativeButton("Annuleren", null)
            .create()

        dialog.setOnShowListener {
            val btnOk = dialog.getButton(DialogInterface.BUTTON_POSITIVE)
            btnOk?.setOnClickListener {
                val user = etUser.text?.toString()?.trim() ?: ""
                val pass = etPass.text?.toString() ?: ""

                if (user.isBlank() || pass.isBlank()) {
                    Toast.makeText(this, "Vul zowel gebruikersnaam als wachtwoord in.", Toast.LENGTH_SHORT).show()
                    return@setOnClickListener
                }

                progress.visibility = View.VISIBLE
                btnOk.isEnabled = false

                lifecycleScope.launch {
                    val result = apiClient.loginWithCredentials(server.host, server.port, user, pass)
                    progress.visibility = View.GONE
                    btnOk.isEnabled = true

                    if (result.isSuccess) {
                        dialog.dismiss()
                        onConnectedSuccess()
                    } else {
                        val err = result.exceptionOrNull()?.message ?: "Inloggen mislukt. Controleer gebruikersnaam en wachtwoord."
                        Toast.makeText(this@ConnectActivity, err, Toast.LENGTH_LONG).show()
                    }
                }
            }
        }
        dialog.show()
    }

    private fun setupQrTab() {
        binding.btnStartQrScan.setOnClickListener {
            val options = ScanOptions().apply {
                setPrompt("Richt uw camera op de QR-code in Spotnet")
                setBeepEnabled(true)
                setOrientationLocked(false)
                setBarcodeImageEnabled(false)
            }
            qrScannerLauncher.launch(options)
        }
    }

    private fun handleScannedQr(scannedContent: String) {
        try {
            val uri = Uri.parse(scannedContent)
            val host = uri.host ?: ""
            val port = if (uri.port > 0) uri.port else 8770
            val pairToken = uri.getQueryParameter("pairToken") ?: ""

            if (host.isBlank()) {
                Toast.makeText(this, "Ongeldige QR-code gescand.", Toast.LENGTH_SHORT).show()
                return
            }

            Toast.makeText(this, "QR-code herkend. Bezig met koppelen…", Toast.LENGTH_SHORT).show()

            lifecycleScope.launch {
                val result = apiClient.pairWithToken(host, port, pairToken)
                if (result.isSuccess) {
                    onConnectedSuccess()
                } else {
                    val err = result.exceptionOrNull()?.message ?: "Koppelen mislukt"
                    Toast.makeText(this@ConnectActivity, err, Toast.LENGTH_LONG).show()
                }
            }
        } catch (e: Exception) {
            Toast.makeText(this, "Fout bij verwerken QR-code: ${e.message}", Toast.LENGTH_SHORT).show()
        }
    }

    private fun setupPinTab() {
        if (prefs.serverHost.isNotBlank()) {
            binding.etHost.setText(prefs.serverHost)
            binding.etPort.setText(prefs.serverPort.toString())
        }

        binding.btnAuthUserPass.isChecked = true

        binding.toggleAuthMode.addOnButtonCheckedListener { _, checkedId, isChecked ->
            if (isChecked) {
                if (checkedId == R.id.btnAuthUserPass) {
                    binding.layoutUserPassFields.visibility = View.VISIBLE
                    binding.layoutPinField.visibility = View.GONE
                    binding.btnConnectWithPin.text = "Inloggen & Verbinden"
                } else {
                    binding.layoutUserPassFields.visibility = View.GONE
                    binding.layoutPinField.visibility = View.VISIBLE
                    binding.btnConnectWithPin.text = "Koppelen & Verbinden"
                }
            }
        }

        binding.btnConnectWithPin.setOnClickListener {
            val host = binding.etHost.text?.toString()?.trim() ?: ""
            val port = binding.etPort.text?.toString()?.toIntOrNull() ?: 8770

            if (host.isBlank()) {
                binding.tilHost.error = "Vul het IP-adres of host van de pc in."
                return@setOnClickListener
            }
            binding.tilHost.error = null

            val isUserPassMode = binding.btnAuthUserPass.isChecked

            binding.progressPin.visibility = View.VISIBLE
            binding.btnConnectWithPin.isEnabled = false

            lifecycleScope.launch {
                val result = if (isUserPassMode) {
                    val user = binding.etUsername.text?.toString()?.trim() ?: ""
                    val pass = binding.etPassword.text?.toString() ?: ""
                    if (user.isBlank() || pass.isBlank()) {
                        binding.progressPin.visibility = View.GONE
                        binding.btnConnectWithPin.isEnabled = true
                        Toast.makeText(this@ConnectActivity, "Vul zowel gebruikersnaam als wachtwoord in.", Toast.LENGTH_SHORT).show()
                        return@launch
                    }
                    apiClient.loginWithCredentials(host, port, user, pass)
                } else {
                    val pin = binding.etPin.text?.toString()?.trim() ?: ""
                    if (pin.isBlank()) {
                        binding.progressPin.visibility = View.GONE
                        binding.btnConnectWithPin.isEnabled = true
                        Toast.makeText(this@ConnectActivity, "Voer de 6-cijferige pincode in.", Toast.LENGTH_SHORT).show()
                        return@launch
                    }
                    apiClient.pairWithPin(host, port, pin)
                }

                binding.progressPin.visibility = View.GONE
                binding.btnConnectWithPin.isEnabled = true

                if (result.isSuccess) {
                    onConnectedSuccess()
                } else {
                    val err = result.exceptionOrNull()?.message ?: "Verbinden mislukt"
                    Toast.makeText(this@ConnectActivity, err, Toast.LENGTH_LONG).show()
                }
            }
        }
    }

    private fun onConnectedSuccess() {
        Toast.makeText(this, "✓ Succesvol gekoppeld met Spotnet!", Toast.LENGTH_SHORT).show()
        if (prefs.notificationsEnabled) {
            SpotnetNotificationWorker.schedule(this, prefs.notificationIntervalMinutes)
        }
        startActivity(Intent(this, MainActivity::class.java))
        finish()
    }

    override fun onDestroy() {
        super.onDestroy()
        discoveryManager.stopDiscovery()
    }
}
