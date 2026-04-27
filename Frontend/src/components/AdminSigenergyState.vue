<template>
  <div class="sigenergy-page">
    <div class="page-header">
      <div>
        <h1>Sigenergy state</h1>
        <p class="description">Latest snapshot stored by the integration daemon.</p>
      </div>
      <div class="header-actions">
        <div v-if="lastRefreshAt" class="last-refresh">
          Last refreshed: {{ formatTimestamp(lastRefreshAt) }}
        </div>
        <button class="refresh-btn" :disabled="loading || refreshing" @click="triggerRefresh">
          {{ refreshing ? 'Refreshing...' : 'Refresh' }}
        </button>
      </div>
    </div>

    <div v-if="loading" class="loading">Loading latest state...</div>
    <div v-else-if="error" class="error">{{ error }}</div>
    <div v-else-if="!state?.hasSnapshot" class="empty">{{ state?.message || 'No snapshot available.' }}</div>
    <div v-else class="state-card">
      <div class="stations-bar">
        <div v-if="stationsError" class="stations-error">{{ stationsError }}</div>
        <div v-else-if="!stationSquares.length" class="stations-empty">No station state available.</div>
        <div
          v-else
          class="stations-squares"
          :style="stationsGridStyle"
          aria-label="Charging station occupancy"
        >
          <span
            v-for="(sq, idx) in stationSquares"
            :key="`${sq.name || 'station'}-${idx}`"
            :class="['station-square', sq.inUse ? 'busy' : 'free']"
            :title="`${sq.name || `Station ${idx + 1}`}: ${sq.inUse ? 'In use' : 'Free'}`"
          >
            <span class="station-name">{{ sq.name || `Station ${sq.number}` }}</span>
            <span class="station-number">{{ sq.number }}</span>
            <span v-if="sq.inUse && sq.loadKw !== null" class="station-load">{{ formatLoadKw(sq.loadKw) }}</span>
          </span>
        </div>
      </div>

      <div class="energy-bars">
        <div class="energy-item">
          <span class="energy-label">Current solar</span>
          <span class="energy-value">{{ formatNumber(state.solarPowerKw, 'kW') }}</span>
        </div>

        <div class="energy-item">
          <span class="energy-label">Charge level</span>
          <span class="energy-value">{{ formatNumber(state.batterySocPercent, '%') }}</span>
        </div>

        <div class="energy-item">
          <span class="energy-label">Available battery</span>
          <span class="energy-value">{{ formatNumber(availableKwhValue(state), 'kWh') }}</span>
        </div>
      </div>

      <div class="state-row">
        <span class="label">Fetched at</span>
        <span class="value">{{ formatTimestamp(state.fetchedAtUtc) }}</span>
      </div>
      <div class="state-row">
        <span class="label">Success</span>
        <span class="value">{{ state.isSuccess ? 'Yes' : 'No' }}</span>
      </div>
      <div class="state-row">
        <span class="label">Configured</span>
        <span class="value">{{ state.configured ? 'Yes' : 'No' }}</span>
      </div>
      <div v-if="state.error" class="state-row">
        <span class="label">Error</span>
        <span class="value error-text">{{ state.error }}</span>
      </div>
      <div class="state-row">
        <span class="label">Battery SOC</span>
        <span class="value">{{ formatNumber(state.batterySocPercent, '%') }}</span>
      </div>
      <div class="state-row">
        <span class="label">Battery power</span>
        <span class="value">{{ formatNumber(state.batteryPowerKw, 'kW') }}</span>
      </div>
      <div class="state-row">
        <span class="label">Solar power</span>
        <span class="value">{{ formatNumber(state.solarPowerKw, 'kW') }}</span>
      </div>
      <div class="state-row">
        <span class="label">Remaining battery</span>
        <span class="value">{{ formatNumber(availableKwhValue(state), 'kWh') }}</span>
      </div>
    </div>
  </div>
</template>

<script>
import { ref, onMounted, computed } from 'vue'
import axios from 'axios'
import { isAppAdmin, roleFromToken } from '../utils/roles'

export default {
  name: 'AdminSigenergyState',
  setup() {
    const state = ref(null)
    const loading = ref(true)
    const refreshing = ref(false)
    const lastRefreshAt = ref(null)
    const error = ref('')
    const stationSquares = ref([])
    const stationsError = ref('')
    const stationsGridStyle = computed(() => {
      const count = Math.max(1, stationSquares.value.length)
      return {
        gridTemplateColumns: `repeat(${count}, minmax(0, 1fr))`
      }
    })


    const token = localStorage.getItem('token')
    const isAdmin = isAppAdmin(roleFromToken(token))

    const fetchState = async () => {
      if (!isAdmin) {
        error.value = 'Only administrators can view the Sigenergy state.'
        loading.value = false
        return
      }

      error.value = ''
      if (!loading.value) refreshing.value = true

      try {
        const response = await axios.get('/api/admin/sigenergy/latest-state', {
          headers: { Authorization: `Bearer ${token}` }
        })
        state.value = response.data
        lastRefreshAt.value = new Date().toISOString()
      } catch (err) {
        error.value = err.response?.status === 403
          ? 'Access denied.'
          : 'Failed to load the latest Sigenergy state.'
      } finally {
        refreshing.value = false
        loading.value = false
      }
    }

    const fetchStationState = async () => {
      if (!isAdmin) return
      stationsError.value = ''
      try {
        const response = await axios.get('/api/chargingstations/smappee-charging-station-display-names', {
          headers: { Authorization: `Bearer ${token}` }
        })
        const squares = Array.isArray(response.data?.squares) ? response.data.squares : []
        stationSquares.value = squares.map((sq, index) => ({
          number: index + 1,
          name: sq.name || `Station ${index + 1}`,
          inUse: isPointInUse(sq.liveState),
          loadKw: Number.isFinite(Number(sq.powerKw)) ? Number(sq.powerKw) : null
        }))
      } catch {
        stationSquares.value = []
        stationsError.value = 'Could not load station state.'
      }
    }

    const triggerRefresh = async () => {
      if (loading.value || refreshing.value) return
      if (!isAdmin) return

      refreshing.value = true
      error.value = ''
      try {
        await axios.post('/api/admin/sigenergy/refresh/energy-flow', null, {
          headers: { Authorization: `Bearer ${token}` }
        })
        await Promise.all([fetchState(), fetchStationState()])
      } catch (err) {
        error.value = err.response?.data?.error || 'Failed to trigger Sigenergy refresh.'
      } finally {
        refreshing.value = false
      }
    }

    const formatTimestamp = (value) => {
      if (!value) return '-'
      return new Date(value).toLocaleString('nl-NL', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit'
      })
    }

    const formatNumber = (value, unit) => {
      if (value === null || value === undefined) return '-'
      return `${Number(value).toFixed(2)} ${unit}`
    }

    const formatLoadKw = (value) => {
      if (value === null || value === undefined || Number.isNaN(Number(value))) return '-'
      return `${Number(value).toFixed(2)} kW`
    }

    const clampPercent = (value) => {
      if (!Number.isFinite(value)) return 0
      return Math.max(0, Math.min(100, value))
    }

    const isPointInUse = (uiState) => {
      const s = String(uiState || '').toLowerCase()
      return s === 'active' || s === 'idle' || s === 'charging' || s === 'preparing' || s === 'suspended'
    }

    // Solar bars are scaled for a typical residential peak around 15 kW.
    const configuredCapacityKwh = (snapshot) => {
      const cap = Number(snapshot?.usableBatteryCapacityKwh)
      return Number.isFinite(cap) && cap > 0 ? cap : null
    }

    // Prefer configured battery capacity from Sigenergy options for available kWh.
    const availableKwhValue = (snapshot) => {
      const soc = Number(snapshot?.batterySocPercent)
      const capacity = configuredCapacityKwh(snapshot)
      if (capacity && Number.isFinite(soc))
        return (capacity * clampPercent(soc)) / 100
      const fallbackCapacity = 45.19
      if (Number.isFinite(soc))
        return (fallbackCapacity * clampPercent(soc)) / 100
      return snapshot?.remainingBatteryKwh ?? null
    }

    onMounted(async () => {
      await Promise.all([fetchState(), fetchStationState()])
    })

    return {
      state,
      loading,
      refreshing,
      lastRefreshAt,
      error,
      stationSquares,
      stationsError,
      stationsGridStyle,
      fetchState,
      triggerRefresh,
      formatTimestamp,
      formatNumber,
      formatLoadKw,
      availableKwhValue
    }
  }
}
</script>

<style scoped>
.sigenergy-page {
  max-width: 800px;
  margin: 0 auto;
  padding: 2rem;
}

.page-header {
  margin-bottom: 1.5rem;
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
}

.page-header h1 {
  margin: 0 0 0.25rem 0;
}

.description {
  color: #666;
  margin: 0;
  font-size: 0.95rem;
}

.header-actions {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.5rem;
}

.last-refresh {
  color: #666;
  font-size: 0.85rem;
}

.refresh-btn {
  padding: 0.5rem 0.9rem;
  border: 1px solid #007bff;
  background: #007bff;
  color: white;
  border-radius: 6px;
  cursor: pointer;
  font-weight: 600;
}

.refresh-btn:hover:not(:disabled) {
  background: #0069d9;
  border-color: #0062cc;
}

.refresh-btn:disabled {
  opacity: 0.65;
  cursor: not-allowed;
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

.state-card {
  background: #fff;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.08);
  overflow: hidden;
}

.stations-bar {
  padding: 1rem;
  border-bottom: 1px solid #e8edf3;
  background: #fff;
}

.stations-error,
.stations-empty {
  color: #6b7280;
  font-size: 0.9rem;
}

.stations-squares {
  display: grid;
  gap: 0.35rem;
  width: 100%;
}

.station-square {
  width: 100%;
  aspect-ratio: 1 / 1;
  border-radius: 4px;
  border: 1px solid rgba(17, 24, 39, 0.2);
  display: flex;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: 0.25rem;
  position: relative;
  overflow: hidden;
  flex-direction: column;
  gap: 0.2rem;
}

.station-name,
.station-number {
  font-weight: 600;
  color: #0f172a;
  font-size: clamp(0.95rem, 2.2vw, 1.6rem);
  line-height: 1.1;
  word-break: break-word;
}

.station-number {
  display: none;
  font-size: clamp(0.7rem, 1.5vw, 0.95rem);
}

.station-load {
  font-size: clamp(0.7rem, 1.35vw, 0.9rem);
  font-weight: 700;
  color: #111827;
  line-height: 1;
}

.station-square.free { background: rgba(34, 197, 94, 0.35); }
.station-square.busy { background: rgba(239, 68, 68, 0.35); }

@media (max-width: 700px) {
  .station-name {
    font-size: 0.72rem;
  }
}

.energy-bars {
  padding: 1rem;
  background: linear-gradient(180deg, #f8fbff 0%, #ffffff 100%);
  border-bottom: 1px solid #e8edf3;
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 0.75rem;
}

.energy-item {
  border-radius: 8px;
  padding: 0.8rem;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  border: 1px solid #e5e7eb;
  background: white;
}

.energy-item:nth-child(1) {
  border-color: #fbd38d;
}

.energy-item:nth-child(2) {
  border-color: #86efac;
}

.energy-item:nth-child(3) {
  border-color: #93c5fd;
}

.energy-label {
  font-weight: 600;
  color: #2d3748;
}

.energy-value {
  font-weight: 700;
  color: #111827;
  font-size: 1.15rem;
}

.state-row {
  display: grid;
  grid-template-columns: 220px 1fr;
  gap: 1rem;
  padding: 0.85rem 1rem;
  border-bottom: 1px solid #f1f3f4;
}

.state-row:last-child {
  border-bottom: none;
}

.label {
  font-weight: 600;
  color: #333;
}

.value {
  color: #333;
  word-break: break-word;
}

.error-text {
  color: #721c24;
}

@media (max-width: 700px) {
  .sigenergy-page {
    padding: 0.5rem;
  }

  .page-header {
    flex-direction: column;
    align-items: stretch;
  }

  .header-actions {
    align-items: stretch;
  }

  .energy-bars {
    grid-template-columns: 1fr;
  }

  .state-row {
    grid-template-columns: 1fr;
    gap: 0.35rem;
  }
}
</style>
