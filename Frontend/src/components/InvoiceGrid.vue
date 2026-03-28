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
          Rate: €0.22 per kWh
        </p>
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
                {{ month.name }}
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
                      <div class="invoice-label">Download PDF</div>
                      <div class="invoice-rate">@€0.22/kWh</div>
                    </a>
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
                Download PDF
              </a>
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

export default {
  name: 'InvoiceGrid',
  setup() {
    const gridData = ref(null)
    const users = ref([])
    const months = ref([])
    const loading = ref(true)
    const error = ref('')
    const selectedUserId = ref(null)

    const selectedUserRow = computed(() => {
      if (!gridData.value || !selectedUserId.value) return null
      return gridData.value.find(row => row.user.id === selectedUserId.value) || null
    })

    const fetchInvoicesGrid = async () => {
      try {
        loading.value = true
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

        // Pre-select first user for the mobile view
        if (response.data.gridData && response.data.gridData.length > 0) {
          selectedUserId.value = response.data.gridData[0].user.id
        }
        
      } catch (err) {
        console.error('Failed to fetch invoices:', err)
        if (err.response?.status === 401) {
          error.value = 'Unauthorized. Please log in again.'
        } else {
          error.value = 'Failed to load invoices. Please try again later.'
        }
      } finally {
        loading.value = false
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

    onMounted(() => {
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
      generatePdfInvoice
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
  background: white;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.invoice-grid {
  width: 100%;
  border-collapse: collapse;
  min-width: 800px;
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
  width: 200px;
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

.invoice-number {
  font-size: 0.8rem;
  font-weight: 600;
}

.invoice-amount {
  font-size: 0.75rem;
  color: #28a745;
  font-weight: 500;
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
</style>