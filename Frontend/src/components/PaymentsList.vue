<template>
  <div class="payments-page">
    <div v-if="loading" class="loading">Loading payments...</div>

    <div v-else-if="error" class="error">{{ error }}</div>

    <div v-else>
      <div class="page-header">
        <div class="header-row">
          <h2>Payments</h2>
          <button class="refresh-btn" :disabled="refreshing" @click="refreshPayments">
            {{ refreshing ? 'Updating...' : 'Refresh statuses' }}
          </button>
        </div>
        <p class="description">All Mollie payment links that have been generated.</p>
      </div>

      <div v-if="payments.length === 0" class="empty">No payments found.</div>

      <div v-else class="table-wrapper">
        <table class="payments-table">
          <thead>
            <tr>
              <th>User</th>
              <th>Period</th>
              <th>Amount</th>
              <th>Status</th>
              <th>Created</th>
              <th>Payment link</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="payment in payments" :key="payment.id">
              <td class="user-cell">{{ payment.userName }}</td>
              <td class="period-cell">{{ formatPeriod(payment.year, payment.month) }}</td>
              <td class="amount-cell">€{{ payment.amount.toFixed(2) }}</td>
              <td class="status-cell">
                <span :class="['status-badge', payment.status]">{{ payment.status }}</span>
              </td>
              <td class="date-cell">{{ formatDate(payment.createdAt) }}</td>
              <td class="link-cell">
                <a v-if="payment.status !== 'paid'" :href="payment.checkoutUrl" target="_blank" rel="noopener noreferrer" class="checkout-link">
                  Open ↗
                </a>
                <span v-else>—</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<script>
import { ref, onMounted } from 'vue'
import axios from 'axios'

export default {
  name: 'PaymentsList',
  setup() {
    const payments = ref([])
    const loading = ref(true)
    const refreshing = ref(false)
    const error = ref('')

    const fetchPayments = async ({ silent = false } = {}) => {
      try {
        if (!silent) loading.value = true
        const token = localStorage.getItem('token')
        if (!token) {
          error.value = 'You must be logged in to view payments'
          return
        }
        const response = await axios.get('/api/invoices/payments', {
          headers: { Authorization: `Bearer ${token}` }
        })
        payments.value = response.data
        error.value = ''
      } catch (err) {
        console.error('Failed to fetch payments:', err)
        if (err.response?.status === 401) {
          error.value = 'Unauthorized. Please log in again.'
        } else {
          error.value = 'Failed to load payments. Please try again later.'
        }
      } finally {
        loading.value = false
      }
    }

    const refreshPayments = async () => {
      refreshing.value = true
      await fetchPayments({ silent: true })
      refreshing.value = false
    }

    const formatPeriod = (year, month) =>
      new Date(year, month - 1, 1).toLocaleDateString('en-US', { month: 'long', year: 'numeric' })

    const formatDate = (iso) =>
      new Date(iso).toLocaleDateString('en-US', { day: 'numeric', month: 'short', year: 'numeric' })

    onMounted(fetchPayments)

    return { payments, loading, refreshing, error, formatPeriod, formatDate, refreshPayments }
  }
}
</script>

<style scoped>
.payments-page {
  max-width: 100%;
  padding: 2rem;
}

.page-header {
  margin-bottom: 1.5rem;
}

.header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.page-header h2 {
  margin: 0 0 0.25rem 0;
  color: #333;
}

.refresh-btn {
  padding: 0.4rem 1rem;
  font-size: 0.875rem;
  background: #4a90d9;
  color: white;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  white-space: nowrap;
}

.refresh-btn:hover:not(:disabled) {
  background: #357abd;
}

.refresh-btn:disabled {
  opacity: 0.6;
  cursor: default;
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
  background-color: #f8d7da;
  color: #721c24;
  padding: 1rem;
  border-radius: 4px;
  border: 1px solid #f5c6cb;
}

.empty {
  color: #6c757d;
  padding: 2rem 0;
}

.table-wrapper {
  overflow-x: auto;
  border-radius: 8px;
  border: 1px solid #dee2e6;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.07);
}

.payments-table {
  width: 100%;
  border-collapse: collapse;
  background: white;
}

.payments-table th {
  background: #f8f9fa;
  font-weight: 600;
  color: #333;
  padding: 0.75rem 1rem;
  text-align: left;
  border-bottom: 2px solid #dee2e6;
  white-space: nowrap;
}

.payments-table td {
  padding: 0.75rem 1rem;
  border-bottom: 1px solid #f1f3f4;
  vertical-align: middle;
}

.payments-table tr:last-child td {
  border-bottom: none;
}

.payments-table tr:hover td {
  background-color: #f8f9fa;
}

.amount-cell {
  font-weight: 600;
  color: #28a745;
}

.status-badge {
  display: inline-block;
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
}

.status-badge.paid {
  background-color: #d4edda;
  color: #155724;
}

.status-badge.open,
.status-badge.pending {
  background-color: #fff3cd;
  color: #856404;
}

.status-badge.failed,
.status-badge.canceled,
.status-badge.expired {
  background-color: #f8d7da;
  color: #721c24;
}

.checkout-link {
  color: #007bff;
  text-decoration: none;
  font-size: 0.9rem;
}

.checkout-link:hover {
  text-decoration: underline;
}

.date-cell {
  color: #6c757d;
  font-size: 0.9rem;
  white-space: nowrap;
}

@media (max-width: 600px) {
  .payments-page {
    padding: 0.75rem;
  }

  .payments-table th,
  .payments-table td {
    padding: 0.6rem 0.5rem;
    font-size: 0.85rem;
  }
}
</style>
