<template>
  <div class="sessions-page">
    <div class="page-header">
      <h1>Charging Sessions</h1>
      <div class="header-actions">
        <button v-if="isAdmin" @click="openAddModal" class="btn btn-success">
          + Add Session
        </button>
        <router-link to="/upload-sessions" class="btn btn-primary">
          Upload CSV
        </router-link>
      </div>
    </div>

    <div class="filters-section">
      <div class="filter-group">
        <label for="month-select">Filter by Month:</label>
        <select 
          id="month-select" 
          v-model="selectedMonth" 
          @change="applyFilter"
          class="month-selector"
          :disabled="loadingMonths"
        >
          <option value="">{{ loadingMonths ? 'Loading months...' : 'All Months' }}</option>
          <option v-for="month in availableMonthsComputed" :key="month.value" :value="month.value">
            {{ month.label }}
          </option>
        </select>
      </div>
      <div v-if="selectedMonth" class="active-filter">
        Showing: {{ getFilterLabel() }}
        <button @click="clearFilter" class="clear-filter">✕</button>
      </div>
    </div>

    <div v-if="loading" class="loading">
      Loading charging sessions...
    </div>

    <div v-else-if="error" class="error">
      {{ error }}
    </div>

    <div v-else class="sessions-container">
      <div class="sessions-summary">
        <div class="summary-card">
          <span class="summary-number">{{ pagination.totalSessions }}</span>
          <span class="summary-label">Total Sessions</span>
        </div>
        <div class="summary-card">
          <span class="summary-number">{{ totalEnergyDelivered.toFixed(1) }}</span>
          <span class="summary-label">Total kWh</span>
        </div>
        <div class="summary-card">
          <span class="summary-number">{{ averageSessionDuration }}</span>
          <span class="summary-label">Avg Duration</span>
        </div>
      </div>

      <div class="sessions-table-container">
        <table class="sessions-table">
          <thead>
            <tr>
              <th>Start Time</th>
              <th>End Time</th>
              <th>Duration</th>
              <th>User</th>
              <th>Station</th>
              <th>Energy (kWh)</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="session in sessions" :key="session.id" class="session-row">
              <td>{{ formatDateTime(session.start) }}</td>
              <td>{{ formatDateTime(session.end) }}</td>
              <td>{{ formatDuration(session.duration) }}</td>
              <td>
                <span class="user-badge">User {{ session.userId }}</span>
              </td>
              <td>
                <span class="station-badge">Station {{ session.stationId }}</span>
              </td>
              <td class="energy-cell">{{ session.chargedKwh }} kWh</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="pagination.totalPages > 1" class="pagination">
        <button 
          @click="changePage(pagination.page - 1)"
          :disabled="pagination.page <= 1"
          class="btn btn-secondary"
        >
          ← Previous
        </button>
        
        <div class="page-info">
          Page {{ pagination.page }} of {{ pagination.totalPages }}
        </div>
        
        <button 
          @click="changePage(pagination.page + 1)"
          :disabled="pagination.page >= pagination.totalPages"
          class="btn btn-secondary"
        >
          Next →
        </button>
      </div>

      <div v-if="sessions.length === 0" class="no-sessions">
        <h3>No charging sessions found</h3>
        <p v-if="selectedMonth">No sessions found for {{ getFilterLabel() }}.</p>
        <p v-else>Upload a CSV file to get started.</p>
        <router-link to="/upload-sessions" class="btn btn-primary">
          Upload Sessions
        </router-link>
      </div>
    </div>
  </div>

  <!-- Add Session Modal -->
  <div v-if="showAddModal" class="modal-overlay" @click.self="closeAddModal">
    <div class="modal">
      <div class="modal-header">
        <h2>Add Charging Session</h2>
        <button class="modal-close" @click="closeAddModal">✕</button>
      </div>
      <form @submit.prevent="submitAddSession" class="modal-body">
        <div class="form-group">
          <label>Start Time</label>
          <input type="datetime-local" v-model="newSession.start" required class="form-input" @change="onStartChange" />
        </div>
        <div class="form-group">
          <label>End Time</label>
          <input type="datetime-local" v-model="newSession.end" required class="form-input" />
        </div>
        <div class="form-group">
          <label>User ID</label>
          <input type="number" v-model.number="newSession.userId" required min="1" class="form-input" />
        </div>
        <div class="form-group">
          <label>Station ID</label>
          <input type="number" v-model.number="newSession.stationId" required min="1" class="form-input" />
        </div>
        <div class="form-group">
          <label>Charged (kWh)</label>
          <input type="number" v-model.number="newSession.chargedKwh" required min="0" step="0.001" class="form-input" />
        </div>
        <div v-if="addError" class="add-error">{{ addError }}</div>
        <div class="modal-actions">
          <button type="button" class="btn btn-secondary" @click="closeAddModal">Cancel</button>
          <button type="submit" class="btn btn-success" :disabled="addLoading">
            {{ addLoading ? 'Saving...' : 'Add Session' }}
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<script>
import { ref, computed, onMounted } from 'vue'
import axios from 'axios'

export default {
  name: 'ChargingSessionsList',
  setup() {
    const sessions = ref([])
    const pagination = ref({
      page: 1,
      pageSize: 50,
      totalSessions: 0,
      totalPages: 0
    })
    const loading = ref(true)
    const error = ref('')
    const selectedMonth = ref('')
    const availableMonths = ref([])
    const loadingMonths = ref(true)
    const isAdmin = ref(false)
    const showAddModal = ref(false)
    const addLoading = ref(false)
    const addError = ref('')
    const newSession = ref({ start: '', end: '', userId: '', stationId: '', chargedKwh: '' })

    const checkAdminRole = () => {
      try {
        const token = localStorage.getItem('token')
        if (!token) return
        const payload = JSON.parse(atob(token.split('.')[1]))
        const role = payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
        isAdmin.value = role === 'Admin'
      } catch {
        isAdmin.value = false
      }
    }

    const openAddModal = () => {
      newSession.value = { start: '', end: '', userId: '', stationId: '', chargedKwh: '' }
      addError.value = ''
      showAddModal.value = true
    }

    const closeAddModal = () => {
      showAddModal.value = false
    }

    const onStartChange = () => {
      if (!newSession.value.end) newSession.value.end = newSession.value.start
    }

    const submitAddSession = async () => {
      try {
        addLoading.value = true
        addError.value = ''
        const token = localStorage.getItem('token')
        await axios.post('/api/chargingsessions', {
          start: new Date(newSession.value.start).toISOString(),
          end: new Date(newSession.value.end).toISOString(),
          userId: newSession.value.userId,
          stationId: newSession.value.stationId,
          chargedKwh: newSession.value.chargedKwh
        }, {
          headers: { Authorization: `Bearer ${token}` }
        })
        closeAddModal()
        fetchSessions(pagination.value.page)
        fetchAvailableMonths()
      } catch (err) {
        addError.value = err.response?.data || 'Failed to add session. Please try again.'
      } finally {
        addLoading.value = false
      }
    }

    const fetchAvailableMonths = async () => {
      try {
        loadingMonths.value = true
        const token = localStorage.getItem('token')
        
        if (!token) {
          return
        }

        const response = await axios.get('/api/chargingsessions/available-months', {
          headers: { Authorization: `Bearer ${token}` }
        })
        
        availableMonths.value = response.data
        console.log('Available months:', response.data)

      } catch (err) {
        console.error('Failed to fetch available months:', err)
      } finally {
        loadingMonths.value = false
      }
    }

    const availableMonthsComputed = computed(() => {
      return availableMonths.value
    })

    const totalEnergyDelivered = computed(() => {
      return sessions.value.reduce((total, session) => total + parseFloat(session.chargedKwh), 0)
    })

    const averageSessionDuration = computed(() => {
      if (sessions.value.length === 0) return '0m'
      const totalMinutes = sessions.value.reduce((total, session) => total + session.duration, 0)
      const avgMinutes = Math.round(totalMinutes / sessions.value.length)
      return formatDuration(avgMinutes)
    })

    const fetchSessions = async (page = 1) => {
      try {
        loading.value = true
        error.value = ''
        
        const token = localStorage.getItem('token')
        
        if (!token) {
          error.value = 'You must be logged in to view charging sessions'
          return
        }

        let url = `/api/chargingsessions?page=${page}&pageSize=${pagination.value.pageSize}`
        
        // Add month filter if selected
        if (selectedMonth.value) {
          const [year, month] = selectedMonth.value.split('-')
          url += `&year=${year}&month=${month}`
          console.log(`Filtering by: ${year}-${month}`, { year, month })
        }

        console.log('Fetching sessions from:', url)

        const response = await axios.get(url, {
          headers: { Authorization: `Bearer ${token}` }
        })
        
        sessions.value = response.data.sessions
        pagination.value = response.data.pagination

      } catch (err) {
        console.error('Failed to fetch charging sessions:', err)
        if (err.response?.status === 401) {
          error.value = 'Unauthorized. Please log in again.'
        } else {
          error.value = 'Failed to load charging sessions. Please try again later.'
        }
      } finally {
        loading.value = false
      }
    }

    const applyFilter = () => {
      // Reset to first page when applying filter
      pagination.value.page = 1
      fetchSessions(1)
    }

    const clearFilter = () => {
      selectedMonth.value = ''
      applyFilter()
    }

    const getFilterLabel = () => {
      if (!selectedMonth.value) return ''
      const [year, month] = selectedMonth.value.split('-')
      const date = new Date(year, month - 1, 1)
      return date.toLocaleDateString('en-US', { 
        year: 'numeric', 
        month: 'long' 
      })
    }

    const changePage = (newPage) => {
      if (newPage >= 1 && newPage <= pagination.value.totalPages) {
        fetchSessions(newPage)
      }
    }

    const formatDateTime = (dateString) => {
      return new Date(dateString).toLocaleString('en-US', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
      })
    }

    const formatDuration = (minutes) => {
      if (minutes < 60) {
        return `${minutes}m`
      }
      const hours = Math.floor(minutes / 60)
      const remainingMinutes = minutes % 60
      return remainingMinutes > 0 ? `${hours}h ${remainingMinutes}m` : `${hours}h`
    }

    onMounted(() => {
      checkAdminRole()
      fetchAvailableMonths()
      fetchSessions()
    })

    return {
      sessions,
      pagination,
      loading,
      error,
      selectedMonth,
      availableMonths,
      availableMonthsComputed,
      loadingMonths,
      totalEnergyDelivered,
      averageSessionDuration,
      isAdmin,
      showAddModal,
      addLoading,
      addError,
      newSession,
      openAddModal,
      closeAddModal,
      submitAddSession,
      onStartChange,
      changePage,
      applyFilter,
      clearFilter,
      getFilterLabel,
      formatDateTime,
      formatDuration
    }
  }
}
</script>

<style scoped>
.sessions-page {
  max-width: 1200px;
  margin: 0 auto;
  padding: 2rem;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 2rem;
  border-bottom: 2px solid #e9ecef;
  padding-bottom: 1rem;
}

.page-header h1 {
  margin: 0;
  color: #333;
}

.header-actions {
  display: flex;
  gap: 1rem;
}

.filters-section {
  background-color: #f8f9fa;
  padding: 1rem;
  border-radius: 8px;
  margin-bottom: 2rem;
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
}

.filter-group {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.filter-group label {
  font-weight: 500;
  color: #333;
  font-size: 0.9rem;
}

.month-selector {
  padding: 0.5rem;
  border: 1px solid #ddd;
  border-radius: 4px;
  font-size: 0.9rem;
  background-color: white;
  min-width: 150px;
}

.month-selector:disabled {
  background-color: #f5f5f5;
  color: #666;
  cursor: not-allowed;
}

.month-selector:focus {
  outline: none;
  border-color: #0066cc;
  box-shadow: 0 0 0 2px rgba(0, 102, 204, 0.2);
}

.active-filter {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  background-color: #0066cc;
  color: white;
  padding: 0.25rem 0.75rem;
  border-radius: 20px;
  font-size: 0.85rem;
}

.clear-filter {
  background: none;
  border: none;
  color: white;
  cursor: pointer;
  padding: 0;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
}

.clear-filter:hover {
  background-color: rgba(255, 255, 255, 0.2);
}

.loading {
  text-align: center;
  padding: 2rem;
  color: #666;
  font-size: 1.1rem;
}

.error {
  background-color: #f8d7da;
  color: #721c24;
  padding: 1rem;
  border-radius: 4px;
  margin-bottom: 2rem;
  border: 1px solid #f5c6cb;
}

.sessions-summary {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1.5rem;
  margin-bottom: 2rem;
}

.summary-card {
  background: white;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  padding: 1.5rem;
  text-align: center;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.summary-number {
  display: block;
  font-size: 2.5rem;
  font-weight: bold;
  color: #007bff;
  margin-bottom: 0.5rem;
}

.summary-label {
  display: block;
  color: #666;
  font-size: 1rem;
}

.sessions-table-container {
  background: white;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
  margin-bottom: 2rem;
}

.sessions-table {
  width: 100%;
  border-collapse: collapse;
}

.sessions-table th {
  background: #f8f9fa;
  padding: 1rem;
  text-align: left;
  font-weight: 600;
  color: #333;
  border-bottom: 2px solid #dee2e6;
}

.session-row {
  transition: background-color 0.2s ease;
}

.session-row:hover {
  background-color: #f8f9fa;
}

.session-row td {
  padding: 1rem;
  border-bottom: 1px solid #e9ecef;
}

.user-badge, .station-badge {
  display: inline-block;
  padding: 0.25rem 0.75rem;
  border-radius: 12px;
  font-size: 0.85rem;
  font-weight: 600;
}

.user-badge {
  background-color: #e3f2fd;
  color: #1565c0;
}

.station-badge {
  background-color: #f3e5f5;
  color: #7b1fa2;
}

.energy-cell {
  font-weight: 600;
  color: #2e7d32;
}

.pagination {
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 1rem;
  margin-bottom: 2rem;
}

.page-info {
  font-weight: 500;
  color: #666;
}

.no-sessions {
  text-align: center;
  padding: 3rem 2rem;
  background: white;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  color: #666;
}

.no-sessions h3 {
  margin: 0 0 1rem 0;
  color: #333;
}

.no-sessions p {
  margin: 0 0 2rem 0;
}

.btn {
  padding: 0.75rem 1.5rem;
  border: none;
  border-radius: 4px;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
  text-decoration: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}

.btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-primary {
  background-color: #007bff;
  color: white;
}

.btn-primary:hover:not(:disabled) {
  background-color: #0056b3;
}

.btn-secondary {
  background-color: #6c757d;
  color: white;
}

.btn-secondary:hover:not(:disabled) {
  background-color: #545b62;
}

.btn-success {
  background-color: #28a745;
  color: white;
}

.btn-success:hover:not(:disabled) {
  background-color: #1e7e34;
}

.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal {
  background: white;
  border-radius: 8px;
  width: 100%;
  max-width: 480px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.2);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1.25rem 1.5rem;
  border-bottom: 1px solid #dee2e6;
}

.modal-header h2 {
  margin: 0;
  font-size: 1.2rem;
  color: #333;
}

.modal-close {
  background: none;
  border: none;
  font-size: 1.1rem;
  cursor: pointer;
  color: #666;
  padding: 0.25rem;
  line-height: 1;
}

.modal-close:hover {
  color: #333;
}

.modal-body {
  padding: 1.5rem;
}

.form-group {
  margin-bottom: 1rem;
}

.form-group label {
  display: block;
  font-weight: 500;
  margin-bottom: 0.4rem;
  font-size: 0.9rem;
  color: #333;
}

.form-input {
  width: 100%;
  padding: 0.5rem 0.75rem;
  border: 1px solid #ced4da;
  border-radius: 4px;
  font-size: 0.9rem;
  box-sizing: border-box;
}

.form-input:focus {
  outline: none;
  border-color: #007bff;
  box-shadow: 0 0 0 2px rgba(0, 123, 255, 0.2);
}

.add-error {
  background-color: #f8d7da;
  color: #721c24;
  padding: 0.75rem;
  border-radius: 4px;
  margin-bottom: 1rem;
  font-size: 0.9rem;
  border: 1px solid #f5c6cb;
}

.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 0.75rem;
  margin-top: 1.25rem;
}

@media (max-width: 768px) {
  .sessions-page {
    padding: 0.75rem;
  }

  .page-header {
    flex-direction: column;
    align-items: flex-start;
    gap: 0.75rem;
    margin-bottom: 1rem;
  }

  .page-header h1 {
    font-size: 1.4rem;
  }

  .filters-section {
    padding: 0.75rem;
    gap: 0.75rem;
  }

  .sessions-summary {
    grid-template-columns: repeat(3, 1fr);
    gap: 0.75rem;
  }

  .summary-card {
    padding: 0.75rem 0.5rem;
  }

  .summary-number {
    font-size: 1.5rem;
  }

  .summary-label {
    font-size: 0.75rem;
  }

  .sessions-table-container {
    overflow-x: auto;
    -webkit-overflow-scrolling: touch;
  }

  .sessions-table {
    min-width: 600px;
  }

  .sessions-table th,
  .session-row td {
    padding: 0.6rem 0.5rem;
    font-size: 0.85rem;
  }

  .pagination {
    gap: 0.5rem;
    flex-wrap: wrap;
  }

  .btn {
    padding: 0.6rem 1rem;
    font-size: 0.85rem;
  }
}
</style>