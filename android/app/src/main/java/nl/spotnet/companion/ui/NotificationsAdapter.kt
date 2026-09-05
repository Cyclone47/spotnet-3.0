package nl.spotnet.companion.ui

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.google.android.material.button.MaterialButton
import nl.spotnet.companion.R
import nl.spotnet.companion.data.NotificationItem

class NotificationsAdapter(
    private val onNotificationClick: (NotificationItem) -> Unit,
    private val onMarkReadClick: (NotificationItem) -> Unit,
    private val onDeleteClick: (NotificationItem) -> Unit
) : RecyclerView.Adapter<NotificationsAdapter.ViewHolder>() {

    private val items = mutableListOf<NotificationItem>()

    fun setItems(newItems: List<NotificationItem>) {
        items.clear()
        items.addAll(newItems)
        notifyDataSetChanged()
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val view = LayoutInflater.from(parent.context)
            .inflate(R.layout.item_notification, parent, false)
        return ViewHolder(view)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        holder.bind(items[position])
    }

    override fun getItemCount(): Int = items.size

    inner class ViewHolder(itemView: View) : RecyclerView.ViewHolder(itemView) {
        private val indicatorUnread: View = itemView.findViewById(R.id.indicatorUnread)
        private val tvRuleType: TextView = itemView.findViewById(R.id.tvRuleType)
        private val tvRuleName: TextView = itemView.findViewById(R.id.tvRuleName)
        private val tvTimeAgo: TextView = itemView.findViewById(R.id.tvTimeAgo)
        private val tvTitle: TextView = itemView.findViewById(R.id.tvTitle)
        private val tvSpotsPreview: TextView = itemView.findViewById(R.id.tvSpotsPreview)
        private val btnMarkRead: MaterialButton = itemView.findViewById(R.id.btnMarkRead)
        private val btnDelete: MaterialButton = itemView.findViewById(R.id.btnDelete)

        fun bind(item: NotificationItem) {
            indicatorUnread.visibility = if (item.isRead) View.GONE else View.VISIBLE
            tvRuleType.text = "[${item.ruleType}]"
            tvRuleName.text = item.ruleName
            tvTimeAgo.text = item.timeAgo.ifBlank { "Zojuist" }
            tvTitle.text = item.title.ifBlank { item.body }

            if (item.spots.isNotEmpty()) {
                val sb = StringBuilder()
                item.spots.take(3).forEach { spot ->
                    sb.append("• ").append(spot.title)
                    if (spot.formattedSize.isNotBlank()) {
                        sb.append(" (").append(spot.formattedSize).append(")")
                    }
                    sb.append("\n")
                }
                if (item.spots.size > 3) {
                    sb.append("... en nog ").append(item.spots.size - 3).append(" spot(s)")
                }
                tvSpotsPreview.text = sb.toString().trim()
                tvSpotsPreview.visibility = View.VISIBLE
            } else {
                tvSpotsPreview.text = item.body
                tvSpotsPreview.visibility = if (item.body.isNotBlank()) View.VISIBLE else View.GONE
            }

            btnMarkRead.visibility = if (item.isRead) View.GONE else View.VISIBLE
            btnMarkRead.setOnClickListener { onMarkReadClick(item) }
            btnDelete.setOnClickListener { onDeleteClick(item) }
            itemView.setOnClickListener { onNotificationClick(item) }
        }
    }
}
