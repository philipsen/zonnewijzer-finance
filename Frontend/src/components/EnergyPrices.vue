<template>
  <div class="energy-prices-page">
    <div class="page-header">
      <h1>Energy Prices</h1>
      <p class="subtitle">Configure the price per kWh per month. Used when generating invoices.</p>
    </div>

    <div v-if="loading" class="loading">Loading...</div>
    <div v-else-if="error" class="error">{{ error }}</div>

    <div v-else>
      <!-- Add / Edit form -->
      <div class="form-card">
        <h2>{{ editingEntry ? 'Edit Price' : 'Add Price' }}</h2>
        <form @submit.prevent="save" class="price-form">
          <div class="form-row">
            <label>Year
              <input v-model.number="form.year" type="number" min="2000" max="2100" required />
            </label>
            <label>Month
              <select v-model.number="form.month" required>
                <option v-for="m in months" :key="m.value" :value="m.value">{{ m.label }}</option>
              </select>
            </label>
            <label>Price per kWh (€)
              <input v-model.number="form.pricePerKwh" type="number" step="0.0001" min="0.0001" required />
            </label>
            <div class="form-actions">
              <button type="submit" class="btn-primary">{{ editingEntry ? 'Update' : 'Save' }}</button>
              <button v-if="editingEntry" type="button" class="btn-secondary" @click="cancelEdit">Cancel</button>
            </div>
          </div>
        </form>
      </div>

      <!-- Prices table -->
      <div class="table-card">
        <table v-if="prices.length" class="prices-table">
          <thead>
            <tr>
              <th>Year</th>
              <th>Month</th>
              <th>Price per kWh</th>
              <th>Last updated</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in prices" :key="`${p.year}-${p.month}`">
              <td>{{ p.year }}</td>
              <td>{{ monthName(p.month) }}</td>
              <td>€{{ p.pricePerKwh.toFixed(4) }}</td>
              <td>{{ formatDate(p.updatedAt || p.createdAt) }}</td>
              <td>
                <button class="btn-edit" @click="startEdit(p)">Edit</button>
                <button class="btn-danger" @click="deletePrice(p)">Delete</button>
              </td>
            </tr>
          </tbody>
        </table>
        <p v-else class="no-data">No energy prices configured yet. The default of €0.22/kWh will be used for invoices.</p>
      </div>
    </div>
  </div>
</template>

<script>
import axios from 'axios'

const MONTH_NAMES = ['January','February','March','April','May','June','July','August','September','October','November','December']

export default {
  name: 'EnergyPrices',
  data() {
    return {
      prices: [],
      loading: true,
      error: null,
      editingEntry: null,
      form: {
        year: new Date().getFullYear(),
        month: new Date().getMonth() + 1,
        pricePerKwh: 0.22
      },
      months: MONTH_NAMES.map((label, i) => ({ value: i + 1, label }))
    }
  },
  async mounted() {
    await this.loadPrices()
  },
  methods: {
    getToken() {
      return localStorage.getItem('token') || ''
    },
    async loadPrices() {
      this.loading = true
      this.error = null
      try {
        const { data } = await axios.get('/api/energyprices', {
          headers: { Authorization: `Bearer ${this.getToken()}` }
        })
        this.prices = data
      } catch (e) {
        this.error = 'Failed to load energy prices.'
      } finally {
        this.loading = false
      }
    },
    startEdit(p) {
      this.editingEntry = p
      this.form = { year: p.year, month: p.month, pricePerKwh: p.pricePerKwh }
    },
    cancelEdit() {
      this.editingEntry = null
      this.form = { year: new Date().getFullYear(), month: new Date().getMonth() + 1, pricePerKwh: 0.22 }
    },
    async save() {
      try {
        await axios.post('/api/energyprices', this.form, {
          headers: { Authorization: `Bearer ${this.getToken()}` }
        })
        this.cancelEdit()
        await this.loadPrices()
      } catch (e) {
        this.error = e.response?.data || 'Failed to save price.'
      }
    },
    async deletePrice(p) {
      if (!confirm(`Delete price for ${this.monthName(p.month)} ${p.year}?`)) return
      try {
        await axios.delete(`/api/energyprices/${p.year}/${p.month}`, {
          headers: { Authorization: `Bearer ${this.getToken()}` }
        })
        await this.loadPrices()
      } catch (e) {
        this.error = 'Failed to delete price.'
      }
    },
    monthName(m) {
      return MONTH_NAMES[m - 1]
    },
    formatDate(dt) {
      if (!dt) return '-'
      return new Date(dt).toLocaleDateString()
    }
  }
}
</script>

<style scoped>
.energy-prices-page {
  padding: 24px;
  max-width: 900px;
  margin: 0 auto;
}

.page-header h1 {
  margin: 0 0 4px;
}

.subtitle {
  color: #6b7280;
  margin: 0 0 24px;
}

.form-card, .table-card {
  background: #fff;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  padding: 24px;
  margin-bottom: 24px;
}

.form-card h2 {
  margin: 0 0 16px;
  font-size: 1.1rem;
}

.form-row {
  display: flex;
  gap: 16px;
  align-items: flex-end;
  flex-wrap: wrap;
}

.form-row label {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 0.875rem;
  font-weight: 500;
  color: #374151;
}

.form-row input,
.form-row select {
  padding: 8px 10px;
  border: 1px solid #d1d5db;
  border-radius: 6px;
  font-size: 0.9rem;
  width: 130px;
}

.form-actions {
  display: flex;
  gap: 8px;
}

.btn-primary {
  padding: 8px 16px;
  background: #2563eb;
  color: #fff;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  font-size: 0.9rem;
}

.btn-primary:hover { background: #1d4ed8; }

.btn-secondary {
  padding: 8px 16px;
  background: #f3f4f6;
  color: #374151;
  border: 1px solid #d1d5db;
  border-radius: 6px;
  cursor: pointer;
  font-size: 0.9rem;
}

.prices-table {
  width: 100%;
  border-collapse: collapse;
}

.prices-table th {
  text-align: left;
  padding: 10px 12px;
  background: #f9fafb;
  border-bottom: 1px solid #e5e7eb;
  font-size: 0.85rem;
  color: #6b7280;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.prices-table td {
  padding: 10px 12px;
  border-bottom: 1px solid #f3f4f6;
  font-size: 0.9rem;
}

.btn-edit {
  padding: 4px 10px;
  background: #f0f9ff;
  color: #0369a1;
  border: 1px solid #bae6fd;
  border-radius: 4px;
  cursor: pointer;
  margin-right: 6px;
  font-size: 0.8rem;
}

.btn-danger {
  padding: 4px 10px;
  background: #fff1f2;
  color: #dc2626;
  border: 1px solid #fecaca;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.8rem;
}

.no-data {
  color: #6b7280;
  font-style: italic;
}

.loading { color: #6b7280; }
.error { color: #dc2626; }
</style>
