<template>
  <div class="event-log-page">
    <div class="page-header">
      <h1>Event Log</h1>
      <p class="description">Activity log of logins, uploads, and invoice downloads.</p>
    </div>

    <div v-if="!isAdmin" class="access-denied">
      <h2>Access Denied</h2>
      <p>Only administrators can view the event log.</p>
    </div>

    <template v-else>
      <div v-if="loading" class="loading">Loading event log...</div>
      <div v-else-if="error" class="error">{{ error }}</div>
      <div v-else-if="logs.length === 0" class="empty">No events recorded yet.</div>
      <div v-else class="table-wrapper">
        <table class="log-table">
          <thead>
            <tr>
              <th>Date &amp; Time</th>
              <th>User</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="log in logs" :key="log.id">
              <td class="timestamp">{{ formatTimestamp(log.timestamp) }}</td>
              <td class="username">{{ log.userName }}</td>
              <td class="action">{{ log.action }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>

<script>
import { ref, onMounted } from 'vue'
import axios from 'axios'

export default {
  name: 'EventLog',
  setup() {
    const logs = ref([])
    const loading = ref(true)
    const error = ref('')

    const isAdmin = ref(false)
    const token = localStorage.getItem('token')
    if (token) {
      try {
        const payload = JSON.parse(atob(token.split('.')[1]))
        const role = payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
        isAdmin.value = role === 'Admin'
      } catch {
        isAdmin.value = false
      }
    }

    const fetchLogs = async () => {
      if (!isAdmin.value) { loading.value = false; return }
      try {
        const response = await axios.get('/api/eventlogs', {
          headers: { Authorization: `Bearer ${token}` }
        })
        logs.value = response.data
      } catch (err) {
        error.value = err.response?.status === 403 ? 'Access denied.' : 'Failed to load event log.'
      } finally {
        loading.value = false
      }
    }

    const formatTimestamp = (ts) => {
      return new Date(ts).toLocaleString('nl-NL', {
        year: 'numeric', month: '2-digit', day: '2-digit',
        hour: '2-digit', minute: '2-digit', second: '2-digit'
      })
    }

    onMounted(fetchLogs)

    return { isAdmin, logs, loading, error, formatTimestamp }
  }
}
</script>

<style scoped>
.event-log-page {
  max-width: 900px;
  margin: 0 auto;
  padding: 2rem;
}

.page-header {
  margin-bottom: 1.5rem;
}

.page-header h1 {
  margin: 0 0 0.25rem 0;
}

.description {
  color: #666;
  margin: 0;
  font-size: 0.95rem;
}

.loading, .empty {
  text-align: center;
  padding: 3rem;
  color: #666;
}

.error {
  background: #f8d7da;
  color: #721c24;
  border: 1px solid #f5c6cb;
  border-radius: 6px;
  padding: 1rem;
}

.access-denied {
  text-align: center;
  padding: 3rem;
  color: #721c24;
  background-color: #f8d7da;
  border: 1px solid #f5c6cb;
  border-radius: 8px;
}

.access-denied h2 {
  margin: 0 0 0.5rem 0;
}

.table-wrapper {
  background: white;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 2px 4px rgba(0,0,0,0.08);
}

.log-table {
  width: 100%;
  border-collapse: collapse;
}

.log-table th {
  background: #f8f9fa;
  padding: 0.75rem 1rem;
  text-align: left;
  font-weight: 600;
  color: #333;
  border-bottom: 2px solid #dee2e6;
  font-size: 0.9rem;
}

.log-table td {
  padding: 0.65rem 1rem;
  border-bottom: 1px solid #f1f3f4;
  font-size: 0.9rem;
  color: #333;
}

.log-table tr:last-child td {
  border-bottom: none;
}

.log-table tr:hover td {
  background: #f8f9fa;
}

.timestamp {
  white-space: nowrap;
  color: #555;
  font-family: monospace;
  font-size: 0.85rem;
}

.username {
  font-weight: 500;
}
</style>
