<template>
  <div class="stations-page">
    <div class="page-header">
      <h1>Charging stations</h1>
      <p class="description">
        Charging points inferred from imported sessions: <strong>station serial + connector</strong> defines a point.
        Legacy rows without serial or connector are grouped by internal id only.
      </p>
    </div>

    <div v-if="!isAdmin" class="access-denied">
      <h2>Access denied</h2>
      <p>Only administrators can view this page.</p>
    </div>

    <template v-else>
      <div v-if="loading" class="loading">Loading stations…</div>
      <div v-else-if="error" class="error">{{ error }}</div>
      <template v-else>
        <div v-if="stations.length === 0" class="empty">No charging sessions yet — nothing to list.</div>
        <div v-else class="summary">
          <span class="summary-count">{{ stations.length }}</span>
          <span class="summary-label">distinct charging points</span>
        </div>
        <div v-if="stations.length > 0" class="table-wrapper">
          <table class="stations-table">
            <thead>
              <tr>
                <th>Station name</th>
                <th>Serial</th>
                <th>Conn.</th>
                <th>Point id</th>
                <th>Sessions</th>
                <th>Last session</th>
                <th>Source</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(row, idx) in stations" :key="rowKey(row, idx)">
                <td>{{ row.chargingStationName || '—' }}</td>
                <td class="mono">{{ row.stationSerialNumber || '—' }}</td>
                <td class="mono">{{ row.connector != null && row.connector !== '' ? row.connector : '—' }}</td>
                <td class="mono">{{ row.stationId }}</td>
                <td>{{ row.sessionCount }}</td>
                <td>{{ formatDateTime(row.lastSessionStart) }}</td>
                <td>
                  <span :class="['source-badge', row.isLegacy ? 'legacy' : 'export']">
                    {{ row.isLegacy ? 'Legacy' : 'Export' }}
                  </span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>
    </template>
  </div>
</template>

<script>
import { ref, onMounted } from 'vue'
import axios from 'axios'

export default {
  name: 'ChargingStationsPage',
  setup() {
    const stations = ref([])
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

    const rowKey = (row, idx) =>
      `${row.stationId}-${row.stationSerialNumber ?? 'x'}-${row.connector ?? 'x'}-${idx}`

    const formatDateTime = (iso) => {
      if (!iso) return '—'
      return new Date(iso).toLocaleString('nl-NL', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit'
      })
    }

    const fetchStations = async () => {
      if (!isAdmin.value) {
        loading.value = false
        return
      }
      try {
        const response = await axios.get('/api/chargingstations', {
          headers: { Authorization: `Bearer ${token}` }
        })
        stations.value = response.data.stations ?? []
      } catch (err) {
        if (err.response?.status === 403) error.value = 'Access denied.'
        else error.value = 'Failed to load charging stations.'
      } finally {
        loading.value = false
      }
    }

    onMounted(fetchStations)

    return { isAdmin, stations, loading, error, formatDateTime, rowKey }
  }
}
</script>

<style scoped>
.stations-page {
  max-width: 1000px;
  margin: 0 auto;
  padding: 2rem;
}

.page-header {
  margin-bottom: 1.5rem;
}

.page-header h1 {
  margin: 0 0 0.5rem 0;
  color: #333;
}

.description {
  margin: 0;
  color: #666;
  line-height: 1.5;
}

.access-denied {
  text-align: center;
  padding: 2rem;
  color: #721c24;
  background: #f8d7da;
  border: 1px solid #f5c6cb;
  border-radius: 8px;
}

.loading,
.error,
.empty {
  padding: 1.5rem;
  text-align: center;
  color: #666;
}

.error {
  color: #721c24;
  background: #f8d7da;
  border-radius: 8px;
}

.summary {
  margin-bottom: 1rem;
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
}

.summary-count {
  font-size: 1.5rem;
  font-weight: 700;
  color: #1565c0;
}

.summary-label {
  color: #666;
  font-size: 0.95rem;
}

.table-wrapper {
  background: #fff;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  overflow-x: auto;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.06);
}

.stations-table {
  width: 100%;
  border-collapse: collapse;
  min-width: 640px;
}

.stations-table th {
  text-align: left;
  padding: 0.85rem 1rem;
  background: #f8f9fa;
  font-weight: 600;
  color: #333;
  border-bottom: 2px solid #dee2e6;
}

.stations-table td {
  padding: 0.75rem 1rem;
  border-bottom: 1px solid #e9ecef;
  vertical-align: middle;
}

.stations-table tbody tr:hover {
  background: #f8f9fa;
}

.mono {
  font-family: ui-monospace, monospace;
  font-size: 0.9rem;
  color: #444;
}

.source-badge {
  display: inline-block;
  padding: 0.2rem 0.55rem;
  border-radius: 6px;
  font-size: 0.8rem;
  font-weight: 600;
}

.source-badge.export {
  background: #e8f5e9;
  color: #2e7d32;
}

.source-badge.legacy {
  background: #fff3e0;
  color: #e65100;
}
</style>
