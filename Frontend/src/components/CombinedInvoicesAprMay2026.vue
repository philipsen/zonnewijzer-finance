<template>
  <div class="combined-invoices-page">
    <div class="page-header">
      <h1>Combined invoices – April &amp; May 2026</h1>
      <p>Download combined PDF invoices or send them to GnuCash. Sessions are loaded live from Smappee (1 Apr – 1 Jun 2026, Brussels time).</p>
    </div>

    <div v-if="loading" class="loading">
      Loading combined invoice data...
    </div>

    <div v-else-if="error" class="error">
      {{ error }}
    </div>

    <div v-else class="content">
      <div v-if="isOwnerRole" class="gnucash-toolbar">
        <span class="gnucash-toolbar-title">GnuCash</span>
        <button
          type="button"
          class="btn-gnucash"
          :disabled="gnucashBulkLoading || !hasInsertableUsers"
          :title="hasInsertableUsers ? '' : 'Every invoice is already in GnuCash, or there are no sessions.'"
          @click="insertGnucashAll"
        >
          {{ gnucashBulkLoading ? '…' : 'Insert all (GnuCash)' }}
        </button>
      </div>

      <div v-if="users.length === 0" class="empty">
        No charging sessions found for April–May 2026.
      </div>

      <div v-else class="table-wrapper">
        <table class="combined-table">
          <thead>
            <tr>
              <th>User</th>
              <th class="num-col">Total kWh</th>
              <th class="num-col">Total €</th>
              <th class="actions-col">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="user in users" :key="user.id">
              <td>
                <div class="user-name">{{ user.name }}</div>
                <div class="user-email">{{ user.email }}</div>
              </td>
              <td class="num-col">{{ user.totalKwh.toFixed(2) }}</td>
              <td class="num-col">€{{ user.totalAmount.toFixed(2) }}</td>
              <td class="actions-col">
                <button
                  type="button"
                  class="btn-pdf"
                  @click="downloadPdf(user.id)"
                >
                  Download PDF
                </button>
                <button
                  v-if="isOwnerRole"
                  type="button"
                  class="btn-gnucash-cell"
                  :disabled="gnucashCellDisabled(user)"
                  :title="user.gnucashInvoiceInserted ? 'Already in GnuCash.' : ''"
                  @click="insertGnucashUser(user.id)"
                >
                  {{ gnucashCellLoading[user.id] ? '…' : 'GnuCash' }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<script>
import { ref, computed, onMounted } from 'vue'
import axios from 'axios'
import { isOwner, roleFromToken } from '../utils/roles'

const PERIOD = {
  startYear: 2026,
  startMonth: 4,
  endYear: 2026,
  endMonth: 5
}

export default {
  name: 'CombinedInvoicesAprMay2026',
  setup () {
    const users = ref([])
    const loading = ref(true)
    const error = ref(null)
    const isOwnerRole = ref(false)
    const gnucashBulkLoading = ref(false)
    const gnucashCellLoading = ref({})

    const periodQuery = () =>
      `startYear=${PERIOD.startYear}&startMonth=${PERIOD.startMonth}&endYear=${PERIOD.endYear}&endMonth=${PERIOD.endMonth}`

    const hasInsertableUsers = computed(() =>
      users.value.some(u => u.hasChargingSessions && !u.gnucashInvoiceInserted)
    )

    const fetchUsers = async () => {
      loading.value = true
      error.value = null
      try {
        const token = localStorage.getItem('token')
        const response = await axios.get(`/api/invoices/combined/sessions?${periodQuery()}`, {
          headers: { Authorization: `Bearer ${token}` }
        })
        users.value = response.data.users || []
      } catch (err) {
        console.error('Failed to load combined invoices:', err)
        error.value = err.response?.data?.message || err.response?.data || 'Failed to load combined invoices'
      } finally {
        loading.value = false
      }
    }

    const downloadPdf = async (userId) => {
      try {
        const token = localStorage.getItem('token')
        const response = await axios.get(
          `/api/invoices/combined/${userId}?${periodQuery()}`,
          {
            headers: { Authorization: `Bearer ${token}` },
            responseType: 'blob'
          }
        )
        const url = window.URL.createObjectURL(new Blob([response.data], { type: 'application/pdf' }))
        const link = document.createElement('a')
        link.href = url
        link.download = `Invoice_combined_${userId}_2026_04_05.pdf`
        document.body.appendChild(link)
        link.click()
        document.body.removeChild(link)
        window.URL.revokeObjectURL(url)
      } catch (err) {
        console.error('Failed to generate combined invoice:', err)
        alert(err.response?.data?.message || 'Failed to generate combined invoice PDF')
      }
    }

    const gnucashCellDisabled = (user) =>
      !!gnucashCellLoading.value[user.id] || !!user.gnucashInvoiceInserted

    const insertGnucash = async (appUserId) => {
      const token = localStorage.getItem('token')
      await axios.post('/api/invoices/combined/gnucash/insert', {
        ...PERIOD,
        appUserId,
        dryRun: false
      }, {
        headers: { Authorization: `Bearer ${token}` }
      })
    }

    const insertGnucashUser = async (userId) => {
      gnucashCellLoading.value = { ...gnucashCellLoading.value, [userId]: true }
      try {
        await insertGnucash(userId)
        await fetchUsers()
      } catch (err) {
        console.error('GnuCash insert failed:', err)
        const msg = err.response?.data?.message || err.response?.data || err.message || 'GnuCash insert failed'
        alert(typeof msg === 'string' ? msg : JSON.stringify(msg))
      } finally {
        gnucashCellLoading.value = { ...gnucashCellLoading.value, [userId]: false }
      }
    }

    const insertGnucashAll = async () => {
      gnucashBulkLoading.value = true
      try {
        await insertGnucash(null)
        await fetchUsers()
      } catch (err) {
        console.error('GnuCash bulk insert failed:', err)
        const msg = err.response?.data?.message || err.response?.data || err.message || 'GnuCash insert failed'
        alert(typeof msg === 'string' ? msg : JSON.stringify(msg))
      } finally {
        gnucashBulkLoading.value = false
      }
    }

    onMounted(() => {
      isOwnerRole.value = isOwner(roleFromToken(localStorage.getItem('token')))
      fetchUsers()
    })

    return {
      users,
      loading,
      error,
      isOwnerRole,
      gnucashBulkLoading,
      gnucashCellLoading,
      hasInsertableUsers,
      downloadPdf,
      gnucashCellDisabled,
      insertGnucashUser,
      insertGnucashAll
    }
  }
}
</script>

<style scoped>
.combined-invoices-page {
  max-width: 960px;
  margin: 0 auto;
  padding: 2rem;
}

.page-header {
  margin-bottom: 2rem;
}

.page-header h1 {
  margin: 0 0 0.5rem 0;
  color: #333;
}

.page-header p {
  margin: 0;
  color: #666;
  font-size: 1.1rem;
}

.loading {
  text-align: center;
  padding: 3rem;
  color: #666;
}

.error {
  background-color: #f8d7da;
  color: #721c24;
  padding: 1rem;
  border-radius: 4px;
  border: 1px solid #f5c6cb;
}

.empty {
  padding: 2rem;
  text-align: center;
  color: #666;
}

.gnucash-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem 1rem;
  margin-bottom: 1.25rem;
  padding: 0.85rem 1rem;
  background: #f0f7ff;
  border: 1px solid #b8daff;
  border-radius: 8px;
}

.gnucash-toolbar-title {
  font-weight: 700;
  color: #004085;
}

.btn-gnucash {
  padding: 0.45rem 0.85rem;
  border: none;
  border-radius: 4px;
  background: #004085;
  color: white;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-gnucash:hover:not(:disabled) {
  background: #002752;
}

.btn-gnucash:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.table-wrapper {
  overflow-x: auto;
}

.combined-table {
  width: 100%;
  border-collapse: collapse;
  background: white;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.08);
}

.combined-table th,
.combined-table td {
  padding: 0.85rem 1rem;
  border-bottom: 1px solid #e9ecef;
  text-align: left;
}

.combined-table th {
  background: #f8f9fa;
  font-weight: 600;
  color: #495057;
}

.num-col {
  text-align: right;
  white-space: nowrap;
}

.actions-col {
  white-space: nowrap;
}

.user-name {
  font-weight: 600;
  color: #333;
}

.user-email {
  font-size: 0.85rem;
  color: #666;
  margin-top: 0.15rem;
}

.btn-pdf,
.btn-gnucash-cell {
  padding: 0.4rem 0.75rem;
  border: none;
  border-radius: 4px;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-pdf {
  background: #007bff;
  color: white;
  margin-right: 0.4rem;
}

.btn-pdf:hover {
  background: #0056b3;
}

.btn-gnucash-cell {
  background: #004085;
  color: white;
}

.btn-gnucash-cell:hover:not(:disabled) {
  background: #002752;
}

.btn-gnucash-cell:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
</style>
