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
      <div class="smappee-servicelocations">
        <div class="smappee-servicelocations-head">
          <h2 class="subsection-title">Smappee service locations</h2>
          <button
            type="button"
            class="btn-update-smappee"
            :disabled="smappeeLoading || smappeeHydrating || smappeeWaitMs > 0"
            :title="smappeeWaitMs > 0 ? smappeeWaitHint : ''"
            @click="refreshSmappeeServiceLocationsFromCloud"
          >
            {{ smappeeButtonLabel }}
          </button>
        </div>
        <p class="subsection-hint">
          The table loads the <strong>last saved snapshot</strong> from the database. <strong>Update from Smappee</strong> fetches
          from the cloud, replaces the snapshot, and is limited to <strong>once every 2 minutes</strong> per user (server and browser).
        </p>
        <p v-if="smappeeError" class="smappee-fetch-error">{{ smappeeError }}</p>
        <p v-if="smappeeUnavailable && !smappeeError" class="smappee-fetch-unavailable">{{ smappeeUnavailable }}</p>

        <div class="table-wrapper smappee-table-wrap">
          <p v-if="smappeeAppName" class="smappee-app-name">App: <strong>{{ smappeeAppName }}</strong></p>
          <p v-if="smappeeFetchedAt" class="smappee-fetched">Fetched {{ smappeeFetchedAt }}</p>
          <table class="stations-table smappee-locations-table">
            <thead>
              <tr>
                <th scope="col">Service location id</th>
                <th scope="col">Name</th>
                <th scope="col">UUID</th>
                <th scope="col">Device serial</th>
                <th scope="col">Lat</th>
                <th scope="col">Lon</th>
                <th scope="col">TZ</th>
                <th scope="col">Appliances (info)</th>
              </tr>
            </thead>
            <tbody>
              <template v-if="smappeeLoading || smappeeHydrating">
                <tr>
                  <td colspan="8" class="smappee-table-placeholder">
                    {{ smappeeHydrating ? 'Loading saved locations…' : 'Refreshing from Smappee…' }}
                  </td>
                </tr>
              </template>
              <template v-else-if="!smappeeRows.length && !smappeeError && !smappeeUnavailable">
                <tr>
                  <td colspan="8" class="smappee-table-placeholder">
                    No saved Smappee locations yet. Click “Update from Smappee” to fetch and store them.
                  </td>
                </tr>
              </template>
              <template v-else-if="!smappeeRows.length">
                <tr>
                  <td colspan="8" class="smappee-table-placeholder">—</td>
                </tr>
              </template>
              <template v-else>
                <tr
                  v-for="(r, idx) in smappeeRows"
                  :key="`sl-${r.serviceLocationId}-${r.deviceSerialNumber ?? ''}-${idx}`"
                >
                  <td class="mono">{{ r.serviceLocationId }}</td>
                  <td>{{ r.name || '—' }}</td>
                  <td class="mono smappee-uuid">{{ r.serviceLocationUuid || '—' }}</td>
                  <td class="mono">{{ r.deviceSerialNumber || '—' }}</td>
                  <td class="mono smappee-num">{{ r.latitude != null ? Number(r.latitude).toFixed(5) : '—' }}</td>
                  <td class="mono smappee-num">{{ r.longitude != null ? Number(r.longitude).toFixed(5) : '—' }}</td>
                  <td class="smappee-tz">{{ r.timezone || '—' }}</td>
                  <td class="smappee-appliances">{{ smappeeApplianceSummary(r) }}</td>
                </tr>
              </template>
            </tbody>
          </table>
        </div>
      </div>

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
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import axios from 'axios'
import { isAppAdmin, roleFromToken } from '../utils/roles'
import { formatLocalYmdHm } from '../utils/formatLocalDateTime'

const SM_APPEE_SL_COOLDOWN_MS = 2 * 60 * 1000
const SM_APPEE_SL_STORAGE_KEY = 'zonnewijzer-smappee-servicelocations-next-allowed-at'

export default {
  name: 'ChargingStationsPage',
  setup() {
    const stations = ref([])
    const loading = ref(true)
    const error = ref('')

    const smappeeRows = ref([])
    const smappeeAppName = ref('')
    const smappeeFetchedAt = ref('')
    const smappeeLoading = ref(false)
    const smappeeHydrating = ref(false)
    const smappeeError = ref('')
    const smappeeUnavailable = ref('')
    const smappeeWaitMs = ref(0)
    let smappeeCooldownTimer = null

    const readNextAllowedAt = () => {
      const raw = sessionStorage.getItem(SM_APPEE_SL_STORAGE_KEY)
      return raw ? Number(raw) : 0
    }

    const scheduleNextAllowedFromNow = () => {
      sessionStorage.setItem(SM_APPEE_SL_STORAGE_KEY, String(Date.now() + SM_APPEE_SL_COOLDOWN_MS))
      refreshSmappeeWait()
    }

    const refreshSmappeeWait = () => {
      const next = readNextAllowedAt()
      smappeeWaitMs.value = next > 0 ? Math.max(0, next - Date.now()) : 0
    }

    const smappeeWaitHint = computed(() => {
      const s = Math.ceil(smappeeWaitMs.value / 1000)
      if (s <= 0) return ''
      const m = Math.floor(s / 60)
      const sec = s % 60
      const label = m > 0 ? `${m}m ${sec}s` : `${sec}s`
      return `Next refresh allowed in ${label}.`
    })

    const smappeeButtonLabel = computed(() => {
      if (smappeeLoading.value || smappeeHydrating.value) return 'Loading…'
      if (smappeeWaitMs.value > 0) {
        const s = Math.ceil(smappeeWaitMs.value / 1000)
        const m = Math.floor(s / 60)
        const sec = s % 60
        return m > 0 ? `Wait ${m}m ${sec}s` : `Wait ${sec}s`
      }
      return 'Update from Smappee'
    })

    const isAdmin = ref(false)
    const token = localStorage.getItem('token')
    isAdmin.value = isAppAdmin(roleFromToken(token))

    const rowKey = (row, idx) =>
      `${row.stationId}-${row.stationSerialNumber ?? 'x'}-${row.connector ?? 'x'}-${idx}`

    const formatDateTime = (iso) => {
      if (!iso) return '—'
      return formatLocalYmdHm(iso) || '—'
    }

    const compareByNameInsensitive = (aName, bName) => {
      const a = (aName ?? '').trim()
      const b = (bName ?? '').trim()
      if (!a && !b) return 0
      if (!a) return 1
      if (!b) return -1
      return a.localeCompare(b, undefined, { sensitivity: 'base' })
    }

    const smappeeApplianceSummary = (row) => {
      const apps = row.appliances
      if (!apps || !apps.length) return '—'
      const parts = apps
        .map((a) => (a ? [a.name, a.type].filter(Boolean).join(' · ') : ''))
        .filter(Boolean)
      return parts.length ? parts.join('; ') : '—'
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
        const list = response.data.stations ?? []
        stations.value = [...list].sort((a, b) => {
          const byName = compareByNameInsensitive(a.chargingStationName, b.chargingStationName)
          if (byName !== 0) return byName
          return compareByNameInsensitive(a.stationSerialNumber, b.stationSerialNumber)
        })
      } catch (err) {
        if (err.response?.status === 403) error.value = 'Access denied.'
        else error.value = 'Failed to load charging stations.'
      } finally {
        loading.value = false
      }
    }

    const applySmappeeLocationsPayload = (data) => {
      const locs = data.serviceLocations ?? []
      smappeeRows.value = [...locs].sort((a, b) => {
        const byName = compareByNameInsensitive(a.name, b.name)
        if (byName !== 0) return byName
        const byId = (a.serviceLocationId ?? 0) - (b.serviceLocationId ?? 0)
        if (byId !== 0) return byId
        return compareByNameInsensitive(a.deviceSerialNumber, b.deviceSerialNumber)
      })
      smappeeAppName.value = data.appName || ''
      if (data.fetchedAt) {
        try {
          smappeeFetchedAt.value = new Date(data.fetchedAt).toLocaleString()
        } catch {
          smappeeFetchedAt.value = ''
        }
      } else {
        smappeeFetchedAt.value = ''
      }
    }

    const hydrateSmappeeFromDb = async () => {
      if (!isAdmin.value || !token) return
      smappeeHydrating.value = true
      smappeeError.value = ''
      smappeeUnavailable.value = ''
      try {
        const { data, status } = await axios.get('/api/chargingstations/smappee-servicelocations', {
          headers: { Authorization: `Bearer ${token}` },
          validateStatus: (s) => s === 200 || s === 403
        })
        if (status === 403) {
          smappeeError.value = 'Access denied.'
          return
        }
        applySmappeeLocationsPayload(data)
      } catch (err) {
        smappeeError.value = err.response?.data?.error || 'Failed to load saved Smappee locations.'
      } finally {
        smappeeHydrating.value = false
      }
    }

    const refreshSmappeeServiceLocationsFromCloud = async () => {
      if (!isAdmin.value || !token) return
      refreshSmappeeWait()
      if (smappeeWaitMs.value > 0) {
        smappeeError.value = smappeeWaitHint.value || 'Refresh is limited to once every 2 minutes.'
        return
      }
      smappeeLoading.value = true
      smappeeError.value = ''
      smappeeUnavailable.value = ''
      let consumedCooldown = false
      try {
        const { data, status } = await axios.post(
          '/api/chargingstations/smappee-servicelocations/refresh',
          {},
          {
            headers: { Authorization: `Bearer ${token}` },
            validateStatus: (s) =>
              s === 200 || s === 429 || s === 502 || s === 503 || s === 403
          }
        )
        if (status === 403) {
          smappeeError.value = 'Access denied.'
          return
        }
        if (status === 429) {
          consumedCooldown = true
          scheduleNextAllowedFromNow()
          smappeeError.value =
            data?.error || 'Smappee service locations can only be refreshed once every 2 minutes.'
          return
        }
        if (status === 503) {
          smappeeUnavailable.value = data?.error || 'Smappee is not configured.'
          return
        }
        consumedCooldown = true
        scheduleNextAllowedFromNow()
        if (status === 502) {
          smappeeError.value = data?.error || 'Could not refresh service locations from Smappee.'
          return
        }
        applySmappeeLocationsPayload(data)
      } catch (err) {
        smappeeError.value = err.response?.data?.error || 'Failed to refresh Smappee service locations.'
        if (err.response?.status === 429) {
          consumedCooldown = true
          scheduleNextAllowedFromNow()
        }
      } finally {
        smappeeLoading.value = false
        if (consumedCooldown) refreshSmappeeWait()
      }
    }

    onMounted(() => {
      fetchStations()
      hydrateSmappeeFromDb()
      refreshSmappeeWait()
      smappeeCooldownTimer = setInterval(refreshSmappeeWait, 1000)
    })

    onBeforeUnmount(() => {
      if (smappeeCooldownTimer) clearInterval(smappeeCooldownTimer)
    })

    return {
      isAdmin,
      stations,
      loading,
      error,
      formatDateTime,
      rowKey,
      smappeeRows,
      smappeeAppName,
      smappeeFetchedAt,
      smappeeLoading,
      smappeeHydrating,
      smappeeError,
      smappeeUnavailable,
      smappeeWaitMs,
      smappeeWaitHint,
      smappeeButtonLabel,
      smappeeApplianceSummary,
      refreshSmappeeServiceLocationsFromCloud
    }
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

.smappee-servicelocations {
  margin-bottom: 2rem;
  padding-bottom: 1.5rem;
  border-bottom: 1px solid #e0e0e0;
}

.smappee-servicelocations-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.subsection-title {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 600;
  color: #333;
}

.subsection-hint {
  margin: 0 0 0.75rem 0;
  font-size: 0.85rem;
  color: #666;
  line-height: 1.45;
}

.subsection-hint code {
  font-size: 0.8rem;
  background: #f0f0f0;
  padding: 0.1rem 0.35rem;
  border-radius: 4px;
}

.btn-update-smappee {
  padding: 0.4rem 0.9rem;
  font-size: 0.9rem;
  font-weight: 600;
  color: #fff;
  background: #1565c0;
  border: none;
  border-radius: 6px;
  cursor: pointer;
}

.btn-update-smappee:hover:not(:disabled) {
  background: #0d47a1;
}

.btn-update-smappee:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

.smappee-fetch-error {
  margin: 0 0 0.75rem 0;
  padding: 0.65rem 0.85rem;
  color: #b71c1c;
  background: #ffebee;
  border: 1px solid #ef9a9a;
  border-radius: 6px;
  font-size: 0.9rem;
}

.smappee-fetch-unavailable {
  margin: 0 0 0.75rem 0;
  padding: 0.65rem 0.85rem;
  color: #5d4000;
  background: #fff8e6;
  border: 1px solid #e6d9a8;
  border-radius: 6px;
  font-size: 0.9rem;
}

.smappee-table-wrap {
  margin-top: 0.5rem;
}

.smappee-app-name,
.smappee-fetched {
  margin: 0 0 0.35rem 0;
  font-size: 0.85rem;
  color: #555;
}

.smappee-uuid {
  font-size: 0.78rem;
  word-break: break-all;
}

.smappee-locations-table {
  min-width: 960px;
}

.smappee-num {
  font-size: 0.78rem;
  white-space: nowrap;
}

.smappee-tz {
  font-size: 0.78rem;
  max-width: 8rem;
  word-break: break-word;
}

.smappee-appliances {
  font-size: 0.78rem;
  max-width: 14rem;
  line-height: 1.35;
}

.smappee-table-placeholder {
  text-align: center;
  color: #757575;
  font-style: italic;
  padding: 1rem 1rem !important;
}
</style>
