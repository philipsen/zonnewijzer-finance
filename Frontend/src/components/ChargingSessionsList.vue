<template>
  <div class="sessions-page">
    <div class="page-header">
      <h1>Charging Sessions</h1>
      <div class="header-actions">
        <button v-if="isAdmin" @click="openAddModal" class="btn btn-success">
          + Add Session
        </button>
        <router-link to="/charging-stations" class="btn btn-secondary">
          Stations
        </router-link>
        <button v-if="isAdmin" type="button" class="btn btn-primary" @click="openSyncModal">
          Sync from Smappee
        </button>
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
              <th>Charge card</th>
              <th>Point ID</th>
              <th>Serial</th>
              <th>Energy (kWh)</th>
              <th v-if="isAdmin">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="session in sessions" :key="session.id" class="session-row">
              <td>{{ formatDateTime(session.start) }}</td>
              <td>
                <span v-if="session.end" class="mono-cell">{{ formatDateTime(session.end) }}</span>
                <span v-else class="ongoing-label">Ongoing</span>
              </td>
              <td>
                <span v-if="session.durationMinutes != null">{{ formatDuration(session.durationMinutes) }}</span>
                <span v-else class="ongoing-label">Ongoing</span>
              </td>
              <td>
                <span class="user-badge">
                  <template v-if="session.chargeCardDisplayName">{{ session.chargeCardDisplayName }}</template>
                  <template v-else-if="session.rfid">{{ session.rfid }}</template>
                  <template v-else>Card {{ session.chargeCardId }}</template>
                </span>
              </td>
              <td class="station-cell">
                <span class="station-name">{{ session.stationId }}</span>
              </td>
              <td class="mono-cell">{{ session.serialNumber || '—' }}</td>
              <td class="energy-cell">{{ session.chargedKwh }} kWh</td>
              <td v-if="isAdmin" class="actions-cell">
                <button class="btn-icon btn-edit" @click="openEditModal(session)" title="Edit">✏️</button>
                <button class="btn-icon btn-delete" @click="openDeleteConfirm(session)" title="Delete">🗑️</button>
              </td>
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
        <p v-else>Use <strong>Sync from Smappee</strong> (admins) to import sessions. The app owner can clear all sessions and charge cards from the <router-link to="/">home</router-link> page.</p>
        <button v-if="isAdmin" type="button" class="btn btn-primary" @click="openSyncModal">
          Sync from Smappee
        </button>
      </div>
    </div>
  </div>

  <!-- Smappee sync modal -->
  <div v-if="showSyncModal" class="modal-overlay" @click.self="closeSyncModal">
    <div class="modal">
      <div class="modal-header">
        <h2>Sync from Smappee</h2>
        <button type="button" class="modal-close" @click="closeSyncModal">✕</button>
      </div>
      <div class="modal-body">
        <p class="field-hint">Imports whitelisted charge cards and charging sessions for the configured service location. Each day is interpreted in Europe/Brussels.</p>
        <div class="form-group">
          <label>From</label>
          <input v-model="syncFrom" type="date" class="form-input" required>
        </div>
        <div class="form-group">
          <label>To</label>
          <input v-model="syncTo" type="date" class="form-input" required>
        </div>
        <div v-if="syncResult" class="sync-result-box">
          <p><strong>{{ syncResult.message }}</strong></p>
          <p>Charge cards: +{{ syncResult.chargeCards.added }} / ~{{ syncResult.chargeCards.updated }}</p>
          <p>Sessions: +{{ syncResult.sessions.added }} / ~{{ syncResult.sessions.updated }}, skipped {{ syncResult.sessions.skipped }}, errors {{ syncResult.sessions.errors }}</p>
        </div>
        <div v-if="syncError" class="add-error">{{ syncError }}</div>
        <div class="modal-actions">
          <button type="button" class="btn btn-secondary" @click="closeSyncModal">Close</button>
          <button type="button" class="btn btn-success" :disabled="syncLoading" @click="submitSync">
            {{ syncLoading ? 'Syncing…' : 'Run sync' }}
          </button>
        </div>
      </div>
    </div>
  </div>

  <!-- Edit Session Modal -->
  <div v-if="showEditModal" class="modal-overlay" @click.self="closeEditModal">
    <div class="modal">
      <div class="modal-header">
        <h2>Edit Charging Session</h2>
        <button class="modal-close" @click="closeEditModal">✕</button>
      </div>
      <form @submit.prevent="submitEditSession" class="modal-body">
        <div class="form-group">
          <label>Start Time</label>
          <input type="datetime-local" v-model="editSession.start" required class="form-input" />
        </div>
        <div class="form-group">
          <label>End Time <span class="optional-label">(optional — empty = ongoing)</span></label>
          <input type="datetime-local" v-model="editSession.end" class="form-input" />
        </div>
        <div class="form-group">
          <label>Charge card ID</label>
          <input type="number" v-model.number="editSession.chargeCardId" required min="1" class="form-input" />
        </div>
        <div class="form-group">
          <label>Station serial number</label>
          <input type="text" v-model="editSession.stationSerialNumber" class="form-input" placeholder="e.g. 6531001272" />
        </div>
        <div class="form-group">
          <label>Connector</label>
          <input type="text" v-model="editSession.connector" class="form-input" placeholder="e.g. 1" />
        </div>
        <div class="form-group">
          <label>Station ID (legacy)</label>
          <input type="number" v-model.number="editSession.stationId" min="1" class="form-input" placeholder="If not using serial + connector" />
          <p class="field-hint">When serial and connector are both filled, the charging point id is derived from them and this field is ignored.</p>
        </div>
        <div class="form-group">
          <label>Charged (kWh)</label>
          <input type="number" v-model.number="editSession.chargedKwh" required min="0" step="0.001" class="form-input" />
        </div>
        <div v-if="editError" class="add-error">{{ editError }}</div>
        <div class="modal-actions">
          <button type="button" class="btn btn-secondary" @click="closeEditModal">Cancel</button>
          <button type="submit" class="btn btn-success" :disabled="editLoading">
            {{ editLoading ? 'Saving...' : 'Save Changes' }}
          </button>
        </div>
      </form>
    </div>
  </div>

  <!-- Delete Confirmation Modal -->
  <div v-if="showDeleteConfirm" class="modal-overlay" @click.self="closeDeleteConfirm">
    <div class="modal modal-sm">
      <div class="modal-header">
        <h2>Delete Session</h2>
        <button class="modal-close" @click="closeDeleteConfirm">✕</button>
      </div>
      <div class="modal-body">
        <p>Are you sure you want to delete this session?</p>
        <p class="delete-detail">
          Card {{ sessionToDelete?.chargeCardId }} ·
          point {{ sessionToDelete?.stationId }} ·
          S/N {{ sessionToDelete?.serialNumber || '?' }} ·
          {{ sessionToDelete?.chargedKwh }} kWh
        </p>
        <div v-if="deleteError" class="add-error">{{ deleteError }}</div>
        <div class="modal-actions">
          <button class="btn btn-secondary" @click="closeDeleteConfirm">Cancel</button>
          <button class="btn btn-danger" @click="submitDelete" :disabled="deleteLoading">
            {{ deleteLoading ? 'Deleting...' : 'Delete' }}
          </button>
        </div>
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
          <input type="datetime-local" v-model="newSession.start" required class="form-input" />
        </div>
        <div class="form-group">
          <label>End Time <span class="optional-label">(optional — empty = ongoing)</span></label>
          <input type="datetime-local" v-model="newSession.end" class="form-input" />
        </div>
        <div class="form-group">
          <label>Charge card ID</label>
          <input type="number" v-model.number="newSession.chargeCardId" required min="1" class="form-input" />
        </div>
        <div class="form-group">
          <label>Station serial number</label>
          <input type="text" v-model="newSession.stationSerialNumber" class="form-input" placeholder="e.g. 6531001272" />
        </div>
        <div class="form-group">
          <label>Connector</label>
          <input type="text" v-model="newSession.connector" class="form-input" placeholder="e.g. 1" />
        </div>
        <div class="form-group">
          <label>Station ID (legacy)</label>
          <input type="number" v-model.number="newSession.stationId" min="1" class="form-input" placeholder="If not using serial + connector" />
          <p class="field-hint">When serial and connector are both filled, the charging point id is derived from them and this field is ignored.</p>
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
import { isAppAdmin, roleFromToken } from '../utils/roles'
import { formatLocalYmdHm } from '../utils/formatLocalDateTime'

const toIsoDateLocal = (d) => {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

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
    const showSyncModal = ref(false)
    const syncFrom = ref('')
    const syncTo = ref('')
    const syncLoading = ref(false)
    const syncError = ref('')
    const syncResult = ref(null)
    const showAddModal = ref(false)
    const addLoading = ref(false)
    const addError = ref('')
    const newSession = ref({
      start: '',
      end: '',
      chargeCardId: '',
      stationSerialNumber: '',
      connector: '',
      stationId: '',
      chargedKwh: ''
    })

    const showEditModal = ref(false)
    const editLoading = ref(false)
    const editError = ref('')
    const editSession = ref({
      id: null,
      start: '',
      end: '',
      chargeCardId: '',
      stationSerialNumber: '',
      connector: '',
      stationId: '',
      chargedKwh: ''
    })

    const showDeleteConfirm = ref(false)
    const deleteLoading = ref(false)
    const deleteError = ref('')
    const sessionToDelete = ref(null)

    const toLocalDatetimeInput = (isoString) => {
      const d = new Date(isoString)
      const pad = (n) => String(n).padStart(2, '0')
      return `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
    }

    const openEditModal = (session) => {
      editSession.value = {
        id: session.id,
        start: toLocalDatetimeInput(session.start),
        end: session.end ? toLocalDatetimeInput(session.end) : '',
        chargeCardId: session.chargeCardId,
        stationSerialNumber: session.serialNumber ?? '',
        connector: '',
        stationId: session.stationId,
        chargedKwh: session.chargedKwh
      }
      editError.value = ''
      showEditModal.value = true
    }

    const buildStationPayload = (sn, cn, legacyId) => {
      const serial = (sn || '').trim()
      const conn = (cn || '').trim()
      const payload = {}
      if (serial && conn) {
        payload.stationSerialNumber = serial
        payload.connector = conn
        payload.stationId = 0
      } else {
        const id = typeof legacyId === 'number' ? legacyId : parseInt(legacyId, 10)
        payload.stationId = Number.isFinite(id) && id >= 1 ? id : 0
        if (serial) payload.stationSerialNumber = serial
        if (conn) payload.connector = conn
      }
      return payload
    }

    const openSyncModal = () => {
      const end = new Date()
      const start = new Date()
      start.setDate(start.getDate() - 90)
      syncFrom.value = toIsoDateLocal(start)
      syncTo.value = toIsoDateLocal(end)
      syncResult.value = null
      syncError.value = ''
      showSyncModal.value = true
    }

    const closeSyncModal = () => {
      showSyncModal.value = false
      syncLoading.value = false
    }

    const submitSync = async () => {
      try {
        syncLoading.value = true
        syncError.value = ''
        syncResult.value = null
        const token = localStorage.getItem('token')
        const { data } = await axios.post('/api/chargingsessions/sync-smappee', {
          from: syncFrom.value,
          to: syncTo.value
        }, {
          headers: { Authorization: `Bearer ${token}` }
        })
        syncResult.value = data
        await fetchSessions(pagination.value.page)
        await fetchAvailableMonths()
      } catch (err) {
        syncError.value = typeof err.response?.data === 'string'
          ? err.response.data
          : err.response?.data?.message || err.message || 'Sync failed.'
      } finally {
        syncLoading.value = false
      }
    }

    const closeEditModal = () => { showEditModal.value = false }

    const submitEditSession = async () => {
      try {
        editLoading.value = true
        editError.value = ''
        const serial = (editSession.value.stationSerialNumber || '').trim()
        const conn = (editSession.value.connector || '').trim()
        const legacyId = editSession.value.stationId
        if (!(serial && conn) && !(Number(legacyId) >= 1)) {
          editError.value = 'Fill station serial + connector together, or enter a station id (≥1) for legacy rows.'
          return
        }
        const token = localStorage.getItem('token')
        const stationPayload = buildStationPayload(
          editSession.value.stationSerialNumber,
          editSession.value.connector,
          editSession.value.stationId
        )
        await axios.put(`/api/chargingsessions/${editSession.value.id}`, {
          start: new Date(editSession.value.start).toISOString(),
          end: editSession.value.end ? new Date(editSession.value.end).toISOString() : null,
          chargeCardId: editSession.value.chargeCardId,
          chargedKwh: editSession.value.chargedKwh,
          ...stationPayload
        }, {
          headers: { Authorization: `Bearer ${token}` }
        })
        closeEditModal()
        fetchSessions(pagination.value.page)
      } catch (err) {
        editError.value = err.response?.data || 'Failed to save changes. Please try again.'
      } finally {
        editLoading.value = false
      }
    }

    const openDeleteConfirm = (session) => {
      sessionToDelete.value = session
      deleteError.value = ''
      showDeleteConfirm.value = true
    }

    const closeDeleteConfirm = () => { showDeleteConfirm.value = false }

    const submitDelete = async () => {
      try {
        deleteLoading.value = true
        deleteError.value = ''
        const token = localStorage.getItem('token')
        await axios.delete(`/api/chargingsessions/${sessionToDelete.value.id}`, {
          headers: { Authorization: `Bearer ${token}` }
        })
        closeDeleteConfirm()
        fetchSessions(pagination.value.page)
        fetchAvailableMonths()
      } catch (err) {
        deleteError.value = err.response?.data || 'Failed to delete session. Please try again.'
      } finally {
        deleteLoading.value = false
      }
    }

    const checkAdminRole = () => {
      isAdmin.value = isAppAdmin(roleFromToken(localStorage.getItem('token')))
    }

    const openAddModal = () => {
      newSession.value = {
        start: '',
        end: '',
        chargeCardId: '',
        stationSerialNumber: '',
        connector: '',
        stationId: '',
        chargedKwh: ''
      }
      addError.value = ''
      showAddModal.value = true
    }

    const closeAddModal = () => {
      showAddModal.value = false
    }

    const submitAddSession = async () => {
      try {
        addLoading.value = true
        addError.value = ''
        const serial = (newSession.value.stationSerialNumber || '').trim()
        const conn = (newSession.value.connector || '').trim()
        const legacyId = newSession.value.stationId
        if (!(serial && conn) && !(Number(legacyId) >= 1)) {
          addError.value = 'Fill station serial + connector together, or enter a station id (≥1) for legacy rows.'
          return
        }
        const token = localStorage.getItem('token')
        const stationPayload = buildStationPayload(
          newSession.value.stationSerialNumber,
          newSession.value.connector,
          newSession.value.stationId
        )
        await axios.post('/api/chargingsessions', {
          start: new Date(newSession.value.start).toISOString(),
          end: newSession.value.end ? new Date(newSession.value.end).toISOString() : null,
          chargeCardId: newSession.value.chargeCardId,
          chargedKwh: newSession.value.chargedKwh,
          ...stationPayload
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
      const completed = sessions.value.filter((s) => s.durationMinutes != null)
      if (completed.length === 0) return sessions.value.length > 0 ? '—' : '0m'
      const totalMinutes = completed.reduce((total, session) => total + session.durationMinutes, 0)
      const avgMinutes = Math.round(totalMinutes / completed.length)
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

    const formatDateTime = (dateString) => formatLocalYmdHm(dateString)

    const formatDuration = (minutes) => {
      if (minutes == null) return ''
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
      showSyncModal,
      syncFrom,
      syncTo,
      syncLoading,
      syncError,
      syncResult,
      openSyncModal,
      closeSyncModal,
      submitSync,
      showAddModal,
      addLoading,
      addError,
      newSession,
      openAddModal,
      closeAddModal,
      submitAddSession,
      showEditModal,
      editLoading,
      editError,
      editSession,
      openEditModal,
      closeEditModal,
      submitEditSession,
      showDeleteConfirm,
      deleteLoading,
      deleteError,
      sessionToDelete,
      openDeleteConfirm,
      closeDeleteConfirm,
      submitDelete,
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

.user-badge {
  display: inline-block;
  padding: 0.25rem 0.75rem;
  border-radius: 12px;
  font-size: 0.85rem;
  font-weight: 600;
  background-color: #e3f2fd;
  color: #1565c0;
}

.station-cell .station-name {
  font-weight: 600;
  color: #333;
}

.mono-cell {
  font-family: ui-monospace, monospace;
  font-size: 0.9rem;
  color: #444;
}

.ongoing-label {
  font-weight: 600;
  color: #1565c0;
  font-size: 0.9rem;
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

.btn-danger {
  background-color: #dc3545;
  color: white;
}

.btn-danger:hover:not(:disabled) {
  background-color: #b02a37;
}

.actions-cell {
  white-space: nowrap;
}

.btn-icon {
  background: none;
  border: none;
  cursor: pointer;
  padding: 0.25rem 0.4rem;
  border-radius: 4px;
  font-size: 1rem;
  line-height: 1;
  transition: background-color 0.15s;
}

.btn-icon:hover {
  background-color: #e9ecef;
}

.modal-sm {
  max-width: 360px;
}

.delete-detail {
  color: #555;
  font-size: 0.9rem;
  margin-top: -0.5rem;
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

.optional-label {
  font-weight: 400;
  color: #888;
  font-size: 0.85rem;
}

.field-hint {
  margin: 0.35rem 0 0 0;
  font-size: 0.8rem;
  color: #666;
  line-height: 1.35;
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

.sync-result-box {
  background: #e8f5e9;
  border: 1px solid #c8e6c9;
  border-radius: 6px;
  padding: 0.75rem 1rem;
  margin-bottom: 1rem;
  font-size: 0.9rem;
  color: #1b5e20;
}

.sync-result-box p {
  margin: 0.25rem 0;
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