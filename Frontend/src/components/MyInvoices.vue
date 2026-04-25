<template>
  <div class="my-invoices-page">
    <div v-if="loading" class="loading">Loading your invoices...</div>

    <div v-else-if="error" class="error">{{ error }}</div>

    <div v-else-if="!hasChargingAccount" class="no-account">
      <h2>My Invoices</h2>
      <p>Your account is not linked to a charging user yet. Please contact an administrator.</p>
    </div>

    <div v-else>
      <div class="page-header">
        <h2>My Invoices</h2>
        <p class="description">Your charging invoices overview, {{ userName }}.</p>
      </div>

      <div v-if="months.length === 0" class="empty">No invoices found.</div>

      <div v-else class="table-wrapper">
        <table class="invoices-table">
          <thead>
            <tr>
              <th>Period</th>
              <th>Invoice #</th>
              <th>kWh</th>
              <th>Rate</th>
              <th>Amount</th>
              <th>Invoice</th>
              <th>Pay</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="m in months" :key="`${m.appUserId}-${m.year}-${m.month}`">
              <td>{{ m.monthName }}</td>
              <td class="invoice-num">{{ m.invoiceNumber }}</td>
              <td class="num-cell">{{ m.totalKwh.toFixed(2) }}</td>
              <td class="num-cell">€{{ m.ratePerKwh.toFixed(2) }}</td>
              <td class="num-cell amount">€{{ m.totalAmount.toFixed(2) }}</td>
              <td class="invoice-cell">
                <a href="#" class="invoice-link" @click.prevent="downloadInvoice(m)">Invoice</a>
              </td>
              <td class="pay-cell">
                <a
                  v-if="canOpenPayment(m)"
                  href="#"
                  class="get-link-btn"
                  @click.prevent="openPayment(m.checkoutUrl)"
                >Pay</a>
                <span v-else-if="m.isPaid" class="paid-text">✓ Paid</span>
                <button
                  v-else
                  class="get-link-btn"
                  :disabled="m._loading"
                  @click="createPaymentLink(m)"
                >{{ m._loading ? '...' : paymentActionLabel(m) }}</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<script>
import { ref, onMounted, onBeforeUnmount } from 'vue'
import axios from 'axios'

export default {
  name: 'MyInvoices',
  setup() {
    const normalizePaymentStatus = (status) =>
      String(status || '').trim().toLowerCase().replace(/^state/, '')

    const canOpenPayment = (month) => {
      const normalizedStatus = normalizePaymentStatus(month.paymentStatus)
      return !month.isPaid && !!month.checkoutUrl && (normalizedStatus === 'open' || normalizedStatus === 'pending')
    }

    const paymentActionLabel = (month) => {
      const normalizedStatus = normalizePaymentStatus(month.paymentStatus)
      if (normalizedStatus === 'expired' || normalizedStatus === 'failed' || normalizedStatus === 'canceled') {
        return 'New link'
      }
      return 'Pay'
    }

    const loading = ref(true)
    const error = ref('')
    const userName = ref('')
    const months = ref([])
    const hasChargingAccount = ref(true)

    const fetchMyInvoices = async () => {
      try {
        loading.value = true
        const token = localStorage.getItem('token')
        if (!token) {
          error.value = 'You must be logged in to view your invoices'
          return
        }
        const response = await axios.get('/api/my-invoices', {
          headers: { Authorization: `Bearer ${token}` }
        })
        userName.value = response.data.userName
        const cardIds = response.data.chargeCardIds
        if (!cardIds || !cardIds.length) {
          hasChargingAccount.value = false
          return
        }
        months.value = response.data.months
      } catch (err) {
        console.error('Failed to fetch invoices:', err)
        if (err.response?.status === 401) {
          error.value = 'Unauthorized. Please log in again.'
        } else {
          error.value = 'Failed to load your invoices. Please try again later.'
        }
      } finally {
        loading.value = false
      }
    }

    const downloadInvoice = async (month) => {
      try {
        const token = localStorage.getItem('token')
        const response = await axios.get(
          `/api/invoices/${month.appUserId}/${month.year}/${month.month}`,
          { headers: { Authorization: `Bearer ${token}` }, responseType: 'blob' }
        )
        const blob = new Blob([response.data], { type: 'application/pdf' })
        const url = window.URL.createObjectURL(blob)
        const link = document.createElement('a')
        link.href = url
        link.download = `Invoice_${month.invoiceNumber}.pdf`
        document.body.appendChild(link)
        link.click()
        document.body.removeChild(link)
        window.URL.revokeObjectURL(url)
        // Refresh row in case payment link was auto-created
        await fetchMyInvoices()
      } catch (err) {
        console.error('Failed to download invoice:', err)
        alert('Failed to download invoice')
      }
    }

    const createPaymentLink = async (month) => {
      month._loading = true
      try {
        const token = localStorage.getItem('token')
        const response = await axios.post(
          `/api/invoices/${month.appUserId}/${month.year}/${month.month}/payment-link`,
          null,
          { headers: { Authorization: `Bearer ${token}` } }
        )
        month.checkoutUrl = response.data.checkoutUrl
        month.paymentStatus = normalizePaymentStatus(response.data.status)
        month.isPaid = month.paymentStatus === 'paid'
        window.open(response.data.checkoutUrl, '_blank')
      } catch (err) {
        console.error('Failed to create payment link:', err)
        alert(err.response?.data || 'Failed to create payment link')
      } finally {
        month._loading = false
      }
    }

    const openPayment = (url) => {
      window.open(url, '_blank')
    }

    const onPaymentMessage = (event) => {
      if (event.origin !== window.location.origin) return
      if (event.data?.type === 'payment-return') {
        fetchMyInvoices()
      }
    }

    onMounted(() => {
      fetchMyInvoices()
      window.addEventListener('message', onPaymentMessage)
    })

    onBeforeUnmount(() => {
      window.removeEventListener('message', onPaymentMessage)
    })

    return {
      loading,
      error,
      userName,
      months,
      hasChargingAccount,
      createPaymentLink,
      downloadInvoice,
      openPayment,
      canOpenPayment,
      paymentActionLabel
    }
  }
}
</script>

<style scoped>
.my-invoices-page {
  max-width: 900px;
  margin: 0 auto;
  padding: 2rem;
}

.page-header {
  margin-bottom: 1.5rem;
}

.page-header h2 {
  margin: 0 0 0.25rem 0;
  color: #333;
}

.description {
  margin: 0;
  color: #666;
  font-size: 0.95rem;
}

.loading {
  text-align: center;
  padding: 3rem;
  color: #666;
}

.error {
  background: #f8d7da;
  color: #721c24;
  padding: 1rem;
  border-radius: 4px;
  border: 1px solid #f5c6cb;
}

.no-account {
  padding: 2rem 0;
  color: #555;
}

.empty {
  color: #6c757d;
  padding: 2rem 0;
}

.table-wrapper {
  overflow-x: auto;
  border-radius: 8px;
  border: 1px solid #dee2e6;
  box-shadow: 0 2px 4px rgba(0,0,0,0.07);
}

.invoices-table {
  width: 100%;
  border-collapse: collapse;
  background: white;
}

.invoices-table th {
  background: #f8f9fa;
  padding: 0.75rem 1rem;
  text-align: left;
  font-size: 0.875rem;
  font-weight: 600;
  color: #495057;
  border-bottom: 2px solid #dee2e6;
}

.invoices-table td {
  padding: 0.75rem 1rem;
  border-bottom: 1px solid #f0f0f0;
  color: #333;
  font-size: 0.9rem;
}

.invoices-table tr:last-child td {
  border-bottom: none;
}

.invoice-num {
  font-family: monospace;
  font-size: 0.82rem;
  color: #666;
}

.num-cell {
  text-align: right;
}

.amount {
  font-weight: 600;
}

.status-badge {
  display: inline-block;
  padding: 0.2rem 0.55rem;
  border-radius: 12px;
  font-size: 0.8rem;
  font-weight: 500;
  text-transform: capitalize;
}

.status-badge.paid    { background: #d4edda; color: #155724; }
.status-badge.open    { background: #fff3cd; color: #856404; }
.status-badge.pending { background: #cce5ff; color: #004085; }
.status-badge.failed,
.status-badge.expired,
.status-badge.canceled { background: #f8d7da; color: #721c24; }
.status-badge.none    { color: #aaa; background: none; }

.pay-link {
  color: #4a90d9;
  text-decoration: none;
  font-weight: 500;
}

.pay-link:hover {
  text-decoration: underline;
}

.paid-text {
  color: #28a745;
  font-weight: 500;
}

.get-link-btn {
  padding: 0.25rem 0.65rem;
  font-size: 0.82rem;
  background: #4a90d9;
  color: white;
  border: none;
  border-radius: 4px;
  cursor: pointer;
}

.get-link-btn:hover:not(:disabled) {
  background: #357abd;
}

.get-link-btn:disabled {
  opacity: 0.6;
  cursor: default;
}

.invoice-link {
  color: #4a90d9;
  text-decoration: none;
  font-weight: 500;
}

.invoice-link:hover {
  text-decoration: underline;
}
</style>
