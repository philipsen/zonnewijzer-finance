<template>
  <div class="invoice-grid-page">

    <div v-if="loading" class="loading">
      Loading invoice data...
    </div>

    <div v-else-if="error" class="error">
      {{ error }}
    </div>

    <div v-else-if="gridData" class="invoice-grid-container">
      <!-- Header -->
      <div class="grid-info">
        <h2>Invoice Grid</h2>
        <p class="description">
          Click on any link to generate and download an invoice PDF based on charging session data.
        </p>
        <div v-if="isOwnerRole && months.length" class="gnucash-toolbar">
          <span class="gnucash-toolbar-title">GnuCash</span>
          <label class="gnucash-field">
            <span>Period</span>
            <select v-model="gnucashPeriodKey" class="gnucash-select">
              <option
                v-for="m in months"
                :key="`${m.year}-${m.month}`"
                :value="`${m.year}-${m.month}`"
              >
                {{ m.name }}
              </option>
            </select>
          </label>
          <button
            type="button"
            class="btn-gnucash"
            :disabled="gnucashBulkLoading || !gnucashMonthHasInsertable"
            :title="gnucashMonthHasInsertable ? '' : 'No sessions to insert, or every invoice for this period is already in GnuCash.'"
            @click="insertGnucashMonth"
          >
            {{ gnucashBulkLoading ? '…' : 'Insert month (all users)' }}
          </button>
        </div>
      </div>

      <!-- Desktop: table grid -->
      <div class="grid-wrapper desktop-grid">
        <table class="invoice-grid">
          <thead>
            <tr>
              <th class="user-column">User</th>
              <th
                v-for="month in months"
                :key="`${month.year}-${month.month}`"
                class="month-column"
              >
                <div>{{ month.name }}</div>
                <div class="month-rate">€{{ month.ratePerKwh.toFixed(2) }}/kWh</div>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="userRow in gridData" :key="userRow.user.id" class="user-row">
              <td class="user-cell">
                <div class="user-info">
                  <div class="user-name">{{ userRow.user.name }}</div>
                  <div class="user-email">{{ userRow.user.email }}</div>
                </div>
              </td>
              <td
                v-for="monthData in userRow.invoices"
                :key="`${userRow.user.id}-${monthData.year}-${monthData.month}`"
                class="invoice-cell"
              >
                <div class="invoice-content">
                  <template v-if="monthData.hasChargingSessions">
                    <a
                      href="#"
                      class="invoice-link"
                      @click.prevent="generatePdfInvoice(userRow.user.id, monthData.year, monthData.month)"
                    >
                      <div class="invoice-kwh">{{ monthData.totalKwh.toFixed(2) }} kWh</div>
                      <div class="invoice-amount">€{{ monthData.totalAmount.toFixed(2) }}</div>
                    </a>
                    <div class="invoice-actions-area">
                      <template v-if="enableOnlinePaymentLinks && !monthData.isOngoingMonth">
                        <template v-if="getPaymentLinkState(userRow.user.id, monthData.year, monthData.month)">
                          <span :class="['payment-status-badge', getPaymentLinkState(userRow.user.id, monthData.year, monthData.month).status]">{{ getPaymentLinkState(userRow.user.id, monthData.year, monthData.month).status }}</span>
                          <a
                            v-if="canOpenPaymentLink(userRow.user.id, monthData.year, monthData.month)"
                            :href="getPaymentLinkState(userRow.user.id, monthData.year, monthData.month).checkoutUrl"
                            target="_blank" rel="noopener noreferrer" class="payment-open-link">Open</a>
                          <button
                            v-else-if="!isPaymentPaid(userRow.user.id, monthData.year, monthData.month)"
                            class="btn-payment-link"
                            :disabled="isPaymentLinkLoading(userRow.user.id, monthData.year, monthData.month)"
                            @click="handlePaymentLink(userRow.user.id, monthData.year, monthData.month)"
                          >
                            {{ isPaymentLinkLoading(userRow.user.id, monthData.year, monthData.month) ? '...' : 'New link' }}
                          </button>
                        </template>
                        <button
                          v-else
                          class="btn-payment-link"
                          :disabled="isPaymentLinkLoading(userRow.user.id, monthData.year, monthData.month)"
                          @click="handlePaymentLink(userRow.user.id, monthData.year, monthData.month)"
                        >
                          {{ isPaymentLinkLoading(userRow.user.id, monthData.year, monthData.month) ? '...' : '💳 Payment link' }}
                        </button>
                      </template>
                      <button
                        v-if="isOwnerRole"
                        type="button"
                        class="btn-gnucash-cell"
                        :disabled="gnucashCellDisabled(userRow.user.id, monthData)"
                        :title="gnucashCellTitle(monthData)"
                        @click="insertGnucashCell(userRow.user.id, monthData.year, monthData.month)"
                      >
                        {{ gnucashCellLoading[gnucashCellKey(userRow.user.id, monthData.year, monthData.month)] ? '…' : 'GnuCash' }}
                      </button>
                      <button
                        v-if="!monthData.isOngoingMonth"
                        type="button"
                        class="btn-email-invoice"
                        :disabled="isEmailLoading(userRow.user.id, monthData.year, monthData.month)"
                        @click="sendInvoiceEmail(userRow.user.id, monthData.year, monthData.month)"
                      >
                        {{ isEmailLoading(userRow.user.id, monthData.year, monthData.month) ? '…' : '✉ Email' }}
                      </button>
                    </div>
                  </template>
                  <template v-else>
                    <div class="no-invoice">—</div>
                  </template>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Mobile: user dropdown + vertical month list -->
      <div class="mobile-view">
        <div class="mobile-user-select">
          <label for="mobile-user-dropdown" class="mobile-label">Select user</label>
          <select
            id="mobile-user-dropdown"
            v-model="selectedUserId"
            class="mobile-select"
          >
            <option v-for="userRow in gridData" :key="userRow.user.id" :value="userRow.user.id">
              {{ userRow.user.name }}
            </option>
          </select>
        </div>

        <div v-if="selectedUserRow" class="mobile-month-list">
          <div
            v-for="monthData in selectedUserRow.invoices"
            :key="`${monthData.year}-${monthData.month}`"
            class="mobile-month-row"
          >
            <span class="mobile-month-name">{{ formatMonthName(monthData.year, monthData.month) }}</span>
            <template v-if="monthData.hasChargingSessions">
              <a
                href="#"
                class="mobile-invoice-link"
                @click.prevent="generatePdfInvoice(selectedUserRow.user.id, monthData.year, monthData.month)"
              >
                <span class="mobile-kwh">{{ monthData.totalKwh.toFixed(2) }} kWh</span>
                <span class="mobile-amount">€{{ monthData.totalAmount.toFixed(2) }}</span>
              </a>
              <div class="invoice-actions-area mobile-invoice-actions">
                <template v-if="enableOnlinePaymentLinks && !monthData.isOngoingMonth">
                  <template v-if="getPaymentLinkState(selectedUserRow.user.id, monthData.year, monthData.month)">
                    <span :class="['payment-status-badge', getPaymentLinkState(selectedUserRow.user.id, monthData.year, monthData.month).status]">{{ getPaymentLinkState(selectedUserRow.user.id, monthData.year, monthData.month).status }}</span>
                    <a
                      v-if="canOpenPaymentLink(selectedUserRow.user.id, monthData.year, monthData.month)"
                      :href="getPaymentLinkState(selectedUserRow.user.id, monthData.year, monthData.month).checkoutUrl"
                      target="_blank" rel="noopener noreferrer" class="payment-open-link">Open</a>
                    <button
                      v-else-if="!isPaymentPaid(selectedUserRow.user.id, monthData.year, monthData.month)"
                      class="btn-payment-link"
                      :disabled="isPaymentLinkLoading(selectedUserRow.user.id, monthData.year, monthData.month)"
                      @click="handlePaymentLink(selectedUserRow.user.id, monthData.year, monthData.month)"
                    >
                      {{ isPaymentLinkLoading(selectedUserRow.user.id, monthData.year, monthData.month) ? '...' : 'New link' }}
                    </button>
                  </template>
                  <button
                    v-else
                    class="btn-payment-link"
                    :disabled="isPaymentLinkLoading(selectedUserRow.user.id, monthData.year, monthData.month)"
                    @click="handlePaymentLink(selectedUserRow.user.id, monthData.year, monthData.month)"
                  >
                    {{ isPaymentLinkLoading(selectedUserRow.user.id, monthData.year, monthData.month) ? '...' : '💳 Pay' }}
                  </button>
                </template>
                <button
                  v-if="isOwnerRole"
                  type="button"
                  class="btn-gnucash-cell mobile-gnucash"
                  :disabled="gnucashCellDisabled(selectedUserRow.user.id, monthData)"
                  :title="gnucashCellTitle(monthData)"
                  @click="insertGnucashCell(selectedUserRow.user.id, monthData.year, monthData.month)"
                >
                  {{ gnucashCellLoading[gnucashCellKey(selectedUserRow.user.id, monthData.year, monthData.month)] ? '…' : 'GnuCash' }}
                </button>
                <button
                  v-if="!monthData.isOngoingMonth"
                  type="button"
                  class="btn-email-invoice mobile-email"
                  :disabled="isEmailLoading(selectedUserRow.user.id, monthData.year, monthData.month)"
                  @click="sendInvoiceEmail(selectedUserRow.user.id, monthData.year, monthData.month)"
                >
                  {{ isEmailLoading(selectedUserRow.user.id, monthData.year, monthData.month) ? '…' : '✉ Email' }}
                </button>
              </div>
            </template>
            <span v-else class="mobile-no-invoice">No sessions</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script>
import { ref, computed, onMounted } from 'vue'
import axios from 'axios'
import { isOwner, roleFromToken } from '../utils/roles'

export default {
  name: 'InvoiceGrid',
  setup() {
    const gridData = ref(null)
    const users = ref([])
    const months = ref([])
    const loading = ref(true)
    const error = ref('')
    const selectedUserId = ref(null)

    const gnucashPeriodKey = ref('')
    const gnucashBulkLoading = ref(false)
    const gnucashCellLoading = ref({})
    const isOwnerRole = ref(false)
    const enableOnlinePaymentLinks = ref(false)

    const selectedUserRow = computed(() => {
      if (!gridData.value || !selectedUserId.value) return null
      return gridData.value.find(row => row.user.id === selectedUserId.value) || null
    })

    const gnucashMonthHasInsertable = computed(() => {
      const parts = gnucashPeriodKey.value.split('-')
      if (parts.length !== 2 || !gridData.value?.length) return false
      const y = parseInt(parts[0], 10)
      const mo = parseInt(parts[1], 10)
      if (Number.isNaN(y) || Number.isNaN(mo)) return false
      for (const row of gridData.value) {
        const inv = row.invoices.find(i => i.year === y && i.month === mo)
        if (inv?.hasChargingSessions && !inv.gnucashInvoiceInserted) return true
      }
      return false
    })

    const fetchInvoicesGrid = async (options = {}) => {
      const silent = options.silent === true
      try {
        if (!silent) loading.value = true
        const token = localStorage.getItem('token')
        
        if (!token) {
          error.value = 'You must be logged in to view invoices'
          return
        }

        const response = await axios.get('/api/invoices', {
          headers: { Authorization: `Bearer ${token}` }
        })
        
        gridData.value = response.data.gridData
        users.value = response.data.users
        months.value = response.data.months
        enableOnlinePaymentLinks.value = response.data.enableOnlinePaymentLinks === true

        if (response.data.months?.length) {
          const last = response.data.months[response.data.months.length - 1]
          gnucashPeriodKey.value = `${last.year}-${last.month}`
        }

        // Pre-select user for the mobile view (keep selection when refetching if still present)
        if (response.data.gridData && response.data.gridData.length > 0) {
          const prev = selectedUserId.value
          const still = prev != null && response.data.gridData.some(r => r.user.id === prev)
          selectedUserId.value = still ? prev : response.data.gridData[0].user.id
        }

        if (enableOnlinePaymentLinks.value) {
          await fetchExistingPayments()
        }
        
      } catch (err) {
        console.error('Failed to fetch invoices:', err)
        if (err.response?.status === 401) {
          error.value = 'Unauthorized. Please log in again.'
        } else {
          error.value = 'Failed to load invoices. Please try again later.'
        }
      } finally {
        if (!silent) loading.value = false
      }
    }

    const generatePdfInvoice = async (userId, year, month) => {
      try {
        const token = localStorage.getItem('token')
        const response = await axios.get(`/api/invoices/${userId}/${year}/${month}`, {
          headers: { Authorization: `Bearer ${token}` },
          responseType: 'blob'
        })
        
        const blob = new Blob([response.data], { type: 'application/pdf' })
        const url = window.URL.createObjectURL(blob)
        const link = document.createElement('a')
        link.href = url
        link.download = `Invoice_User${userId}_${year}-${String(month).padStart(2, '0')}.pdf`
        document.body.appendChild(link)
        link.click()
        document.body.removeChild(link)
        window.URL.revokeObjectURL(url)
        
      } catch (err) {
        console.error('Failed to generate invoice:', err)
        alert(err.response?.data?.message || 'Failed to generate invoice PDF')
      }
    }

    const formatMonthName = (year, month) => {
      return new Date(year, month - 1, 1).toLocaleDateString('en-US', { month: 'long', year: 'numeric' })
    }

    // Payment links state: keyed by `${userId}_${year}_${month}`
    const paymentLinks = ref({})
    const paymentLinksLoading = ref({})
    const emailLoading = ref({})
    const normalizePaymentStatus = (status) =>
      String(status || '').trim().toLowerCase().replace(/^state/, '')

    const paymentLinkKey = (userId, year, month) => `${userId}_${year}_${month}`

    const getPaymentLinkState = (userId, year, month) =>
      paymentLinks.value[paymentLinkKey(userId, year, month)] || null

    const canOpenPaymentLink = (userId, year, month) => {
      const state = getPaymentLinkState(userId, year, month)
      if (!state?.checkoutUrl) return false
      return state.status === 'open' || state.status === 'pending'
    }

    const isPaymentPaid = (userId, year, month) => {
      const state = getPaymentLinkState(userId, year, month)
      return state?.status === 'paid'
    }

    const isPaymentLinkLoading = (userId, year, month) =>
      !!paymentLinksLoading.value[paymentLinkKey(userId, year, month)]

    const isEmailLoading = (userId, year, month) =>
      !!emailLoading.value[paymentLinkKey(userId, year, month)]

    const sendInvoiceEmail = async (userId, year, month) => {
      const key = paymentLinkKey(userId, year, month)
      emailLoading.value = { ...emailLoading.value, [key]: true }
      try {
        const token = localStorage.getItem('token')
        const response = await axios.post(`/api/invoices/${userId}/${year}/${month}/send-email`, null, {
          headers: { Authorization: `Bearer ${token}` }
        })
        alert(response.data?.message || 'Invoice sent by email.')
      } catch (err) {
        console.error('Failed to send invoice email:', err)
        const msg = err.response?.data?.message || err.response?.data || 'Failed to send invoice email'
        alert(typeof msg === 'string' ? msg : JSON.stringify(msg))
      } finally {
        emailLoading.value = { ...emailLoading.value, [key]: false }
      }
    }

    const handlePaymentLink = async (userId, year, month) => {
      const key = paymentLinkKey(userId, year, month)
      paymentLinksLoading.value = { ...paymentLinksLoading.value, [key]: true }
      try {
        const token = localStorage.getItem('token')
        const response = await axios.post(`/api/invoices/${userId}/${year}/${month}/payment-link`, null, {
          headers: { Authorization: `Bearer ${token}` }
        })
        paymentLinks.value = {
          ...paymentLinks.value,
          [key]: {
            ...response.data,
            status: normalizePaymentStatus(response.data.status)
          }
        }
        window.open(response.data.checkoutUrl, '_blank', 'noopener,noreferrer')
      } catch (err) {
        console.error('Failed to create payment link:', err)
        alert(err.response?.data || 'Failed to create payment link')
      } finally {
        paymentLinksLoading.value = { ...paymentLinksLoading.value, [key]: false }
      }
    }

    const gnucashCellKey = (userId, year, month) => `${userId}_${year}_${month}`

    const gnucashCellDisabled = (userId, monthData) => {
      const key = gnucashCellKey(userId, monthData.year, monthData.month)
      return !!gnucashCellLoading.value[key] || !!monthData.gnucashInvoiceInserted
    }

    const gnucashCellTitle = (monthData) =>
      monthData.gnucashInvoiceInserted ? 'This invoice is already in GnuCash.' : ''

    const insertGnucashMonth = async () => {
      const parts = gnucashPeriodKey.value.split('-')
      if (parts.length !== 2) {
        alert('Select a period.')
        return
      }
      const year = parseInt(parts[0], 10)
      const month = parseInt(parts[1], 10)
      gnucashBulkLoading.value = true
      try {
        const token = localStorage.getItem('token')
        await axios.post('/api/invoices/gnucash/insert', {
          year,
          month,
          appUserId: null,
          dryRun: false
        }, {
          headers: { Authorization: `Bearer ${token}` }
        })
        await fetchInvoicesGrid({ silent: true })
      } catch (err) {
        console.error('GnuCash insert failed:', err)
        const msg = err.response?.data?.message || err.response?.data || err.message || 'GnuCash insert failed'
        alert(typeof msg === 'string' ? msg : JSON.stringify(msg))
      } finally {
        gnucashBulkLoading.value = false
      }
    }

    const insertGnucashCell = async (userId, year, month) => {
      const key = gnucashCellKey(userId, year, month)
      gnucashCellLoading.value = { ...gnucashCellLoading.value, [key]: true }
      try {
        const token = localStorage.getItem('token')
        await axios.post('/api/invoices/gnucash/insert', {
          year,
          month,
          appUserId: userId,
          dryRun: false
        }, {
          headers: { Authorization: `Bearer ${token}` }
        })
        await fetchInvoicesGrid({ silent: true })
      } catch (err) {
        console.error('GnuCash insert failed:', err)
        const msg = err.response?.data?.message || err.response?.data || err.message || 'GnuCash insert failed'
        alert(typeof msg === 'string' ? msg : JSON.stringify(msg))
      } finally {
        const next = { ...gnucashCellLoading.value }
        delete next[key]
        gnucashCellLoading.value = next
      }
    }

    const fetchExistingPayments = async () => {
      try {
        const token = localStorage.getItem('token')
        if (!token) return
        const response = await axios.get('/api/invoices/payments', {
          headers: { Authorization: `Bearer ${token}` }
        })
        const map = {}
        for (const p of response.data) {
          map[paymentLinkKey(p.appUserId, p.year, p.month)] = {
            checkoutUrl: p.checkoutUrl,
            paymentId: p.molliePaymentId,
            status: normalizePaymentStatus(p.status)
          }
        }
        paymentLinks.value = map
      } catch (err) {
        console.error('Failed to load existing payments:', err)
      }
    }

    onMounted(() => {
      isOwnerRole.value = isOwner(roleFromToken(localStorage.getItem('token')))
      fetchInvoicesGrid()
    })

    return {
      gridData,
      users,
      months,
      loading,
      error,
      selectedUserId,
      selectedUserRow,
      formatMonthName,
      generatePdfInvoice,
      paymentLinks,
      getPaymentLinkState,
      canOpenPaymentLink,
      isPaymentPaid,
      isPaymentLinkLoading,
      isEmailLoading,
      sendInvoiceEmail,
      handlePaymentLink,
      gnucashPeriodKey,
      gnucashBulkLoading,
      gnucashCellLoading,
      gnucashCellKey,
      gnucashMonthHasInsertable,
      gnucashCellDisabled,
      gnucashCellTitle,
      insertGnucashMonth,
      insertGnucashCell,
      isOwnerRole,
      enableOnlinePaymentLinks
    }
  }
}
</script>

<style scoped>
.invoice-grid-page {
  max-width: 100%;
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

.gnucash-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem 1rem;
  margin-top: 1rem;
  padding: 0.85rem 1rem;
  background: #f0f7ff;
  border: 1px solid #b8daff;
  border-radius: 8px;
}

.gnucash-toolbar-title {
  font-weight: 700;
  color: #004085;
  margin-right: 0.25rem;
}

.gnucash-field {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.9rem;
  color: #333;
}

.gnucash-select {
  padding: 0.35rem 0.5rem;
  border: 1px solid #ced4da;
  border-radius: 4px;
  font-size: 0.9rem;
  background: white;
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

.btn-gnucash-cell {
  margin-top: 0.35rem;
  width: 100%;
  padding: 0.3rem 0.4rem;
  font-size: 0.75rem;
  border: 1px solid #004085;
  border-radius: 4px;
  background: white;
  color: #004085;
  cursor: pointer;
}

.btn-gnucash-cell:hover:not(:disabled) {
  background: #e7f1ff;
}

.btn-gnucash-cell:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-email-invoice {
  margin-top: 0.35rem;
  width: 100%;
  padding: 0.3rem 0.4rem;
  font-size: 0.75rem;
  border: 1px solid #6c757d;
  border-radius: 4px;
  background: white;
  color: #495057;
  cursor: pointer;
}

.btn-email-invoice:hover:not(:disabled) {
  background: #495057;
  color: white;
}

.btn-email-invoice:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.mobile-email {
  margin-top: 0.5rem;
}

.mobile-gnucash {
  margin-top: 0.5rem;
}

.admin-controls {
  background: #f8f9fa;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  padding: 1.5rem;
  margin-bottom: 2rem;
}

.generation-section {
  margin-bottom: 2rem;
}

.generation-section:last-child {
  margin-bottom: 0;
}

.generation-section h3 {
  margin: 0 0 1rem 0;
  color: #333;
  font-size: 1.1rem;
}

.generate-form {
  display: flex;
  gap: 1rem;
  align-items: center;
  flex-wrap: wrap;
}

.bulk-result {
  margin-top: 1rem;
  padding: 1rem;
  background: #e9ecef;
  border-radius: 4px;
}

.bulk-result h4 {
  margin: 0 0 0.5rem 0;
  color: #333;
  font-size: 1rem;
}

.result-summary {
  display: flex;
  gap: 1rem;
  margin-bottom: 0.5rem;
  flex-wrap: wrap;
}

.result-stat {
  padding: 0.25rem 0.5rem;
  border-radius: 3px;
  font-size: 0.85rem;
  font-weight: 600;
}

.result-stat.generated {
  background: #d4edda;
  color: #155724;
}

.result-stat.skipped {
  background: #fff3cd;
  color: #856404;
}

.result-stat.errors {
  background: #f8d7da;
  color: #721c24;
}

.form-select,
.form-input {
  padding: 0.5rem;
  border: 1px solid #ced4da;
  border-radius: 4px;
  font-size: 0.9rem;
}

.amount-input {
  width: 120px;
}

.btn {
  padding: 0.5rem 1rem;
  border: none;
  border-radius: 4px;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
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

.btn-link {
  background: none;
  color: #007bff;
  text-decoration: underline;
  border: none;
  padding: 0;
  font-size: 0.85rem;
}

.btn-link:hover {
  color: #0056b3;
  text-decoration: none;
}

.grid-wrapper {
  overflow-x: auto;
  display: inline-block;
  background: white;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.invoice-grid {
  width: auto;
  border-collapse: collapse;
}

.invoice-grid th,
.invoice-grid td {
  border: 1px solid #dee2e6;
  text-align: center;
  vertical-align: middle;
}

.invoice-grid th {
  background-color: #f8f9fa;
  font-weight: 600;
  color: #333;
  padding: 0.75rem 0.5rem;
  position: sticky;
  top: 0;
  z-index: 10;
}

.user-column {
  width: 100px;
  text-align: left;
  position: sticky;
  left: 0;
  background-color: #f8f9fa !important;
  z-index: 11;
}

.month-column {
  width: 140px;
  font-size: 0.85rem;
}

.month-rate {
  font-size: 0.75rem;
  font-weight: 400;
  color: #6b7280;
  margin-top: 2px;
}

.user-row:nth-child(even) {
  background-color: #f8f9fa;
}

.user-cell {
  text-align: left;
  padding: 1rem;
  position: sticky;
  left: 0;
  background-color: inherit;
  z-index: 5;
}

.user-info {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.user-name {
  font-weight: 600;
  color: #333;
  font-size: 0.9rem;
}

.user-email {
  font-size: 0.8rem;
  color: #666;
}

.invoice-cell {
  padding: 0.5rem;
  width: 140px;
}

.invoice-content {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.25rem;
}

.invoice-link {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.25rem;
  text-decoration: none;
  color: #007bff;
  padding: 0.5rem;
  border-radius: 4px;
  transition: background-color 0.2s ease;
  width: 100%;
}

.invoice-link:hover {
  background-color: #f0f8ff;
  text-decoration: none;
}

.invoice-kwh {
  font-size: 0.75rem;
  color: #6b7280;
}

.invoice-amount {
  font-size: 0.95rem;
  color: #28a745;
  font-weight: 700;
}

.no-invoice {
  color: #6c757d;
  font-size: 1.2rem;
  opacity: 0.5;
}

/* Mobile view – hidden by default, shown via media query */
.mobile-view {
  display: none;
}

@media (max-width: 768px) {
  .invoice-grid-page {
    padding: 0.75rem;
  }

  .grid-info h2 {
    font-size: 1.2rem;
    margin-bottom: 0.25rem;
  }

  .grid-info .description {
    font-size: 0.9rem;
  }

  .gnucash-toolbar {
    flex-direction: column;
    align-items: stretch;
  }

  .btn-gnucash {
    width: 100%;
  }

  .desktop-grid {
    display: none;
  }

  .mobile-view {
    display: block;
  }
}

.mobile-user-select {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  margin-bottom: 1.25rem;
}

.mobile-label {
  font-weight: 600;
  color: #333;
  font-size: 0.9rem;
}

.mobile-select {
  padding: 0.65rem 0.75rem;
  border: 1px solid #ced4da;
  border-radius: 6px;
  font-size: 1rem;
  background-color: white;
  width: 100%;
}

.mobile-select:focus {
  outline: none;
  border-color: #007bff;
  box-shadow: 0 0 0 2px rgba(0, 123, 255, 0.2);
}

.mobile-month-list {
  background: white;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.08);
}

.mobile-month-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.85rem 1rem;
  border-bottom: 1px solid #f1f3f4;
}

.mobile-month-row:last-child {
  border-bottom: none;
}

.mobile-month-name {
  font-weight: 500;
  color: #333;
  font-size: 0.95rem;
}

.mobile-invoice-link {
  color: #007bff;
  text-decoration: none;
  font-weight: 600;
  font-size: 0.9rem;
  padding: 0.35rem 0.75rem;
  border: 1px solid #007bff;
  border-radius: 4px;
  transition: background-color 0.15s ease, color 0.15s ease;
}

.mobile-invoice-link:hover {
  background-color: #007bff;
  color: white;
}

.mobile-no-invoice {
  color: #adb5bd;
  font-size: 0.85rem;
}

.payment-link-area,
.invoice-actions-area {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  flex-wrap: wrap;
  justify-content: center;
  margin-top: 0.25rem;
}

.mobile-payment-link-area,
.mobile-invoice-actions {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  flex-shrink: 0;
}

.btn-payment-link {
  font-size: 0.7rem;
  padding: 0.2rem 0.45rem;
  border: 1px solid #6c757d;
  border-radius: 4px;
  background: white;
  color: #495057;
  cursor: pointer;
  white-space: nowrap;
  transition: background-color 0.15s ease, color 0.15s ease;
}

.btn-payment-link:hover:not(:disabled) {
  background-color: #495057;
  color: white;
}

.btn-payment-link:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.payment-status-badge {
  font-size: 0.65rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  padding: 0.15rem 0.35rem;
  border-radius: 3px;
}

.payment-status-badge.paid {
  background-color: #d4edda;
  color: #155724;
}

.payment-status-badge.open,
.payment-status-badge.pending {
  background-color: #fff3cd;
  color: #856404;
}

.payment-status-badge.failed,
.payment-status-badge.canceled,
.payment-status-badge.expired {
  background-color: #f8d7da;
  color: #721c24;
}

.payment-open-link {
  font-size: 0.7rem;
  color: #007bff;
  text-decoration: none;
}

.payment-open-link:hover {
  text-decoration: underline;
}
</style>