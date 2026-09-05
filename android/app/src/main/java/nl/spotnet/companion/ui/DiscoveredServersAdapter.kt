package nl.spotnet.companion.ui

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.google.android.material.button.MaterialButton
import nl.spotnet.companion.R
import nl.spotnet.companion.data.DiscoveredServer

class DiscoveredServersAdapter(
    private val onConnectClicked: (DiscoveredServer) -> Unit
) : RecyclerView.Adapter<DiscoveredServersAdapter.ViewHolder>() {

    private val items = mutableListOf<DiscoveredServer>()

    fun setServers(newServers: List<DiscoveredServer>) {
        items.clear()
        items.addAll(newServers)
        notifyDataSetChanged()
    }

    fun addServer(server: DiscoveredServer) {
        val existingIndex = items.indexOfFirst { it.host == server.host && it.port == server.port }
        if (existingIndex == -1) {
            items.add(server)
            notifyItemInserted(items.size - 1)
        } else {
            items[existingIndex] = server
            notifyItemChanged(existingIndex)
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val view = LayoutInflater.from(parent.context)
            .inflate(R.layout.item_discovered_server, parent, false)
        return ViewHolder(view)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        holder.bind(items[position])
    }

    override fun getItemCount(): Int = items.size

    inner class ViewHolder(itemView: View) : RecyclerView.ViewHolder(itemView) {
        private val tvName: TextView = itemView.findViewById(R.id.tvServerName)
        private val tvAddress: TextView = itemView.findViewById(R.id.tvServerAddress)
        private val tvVersion: TextView = itemView.findViewById(R.id.tvServerVersion)
        private val btnConnect: MaterialButton = itemView.findViewById(R.id.btnConnect)

        fun bind(server: DiscoveredServer) {
            tvName.text = if (server.machine.isNotBlank() && server.machine != server.host) {
                "${server.name} (${server.machine})"
            } else {
                server.name
            }
            tvAddress.text = "${server.host}:${server.port}"
            tvVersion.text = "Spotnet v${server.version}"

            btnConnect.setOnClickListener { onConnectClicked(server) }
            itemView.setOnClickListener { onConnectClicked(server) }
        }
    }
}
