<template>
  <div class="corrections-page">
    <div class="page-header">
      <h1>Invoice corrections</h1>
      <p class="subtitle">
        A correction has a date, a user, a description and an amount.
        The amount includes 21% BTW. On the invoice it is shown as the amount excluding BTW, the BTW, and the total.
        It is added to that user's invoice for the month of the date. A negative amount reduces the invoice.
      </p>
      <router-link to="/invoices" class="back-link">Back to invoices</router-link>
    </div>

    <div v-if="loading" class="loading">Loading...</div>
    <div v-else-if="error" class="error">{{ error }}</div>

    <div v-else>
      <div class="form-card">
        <h2>{{ editingId ? 'Edit correction' : 'Add correction' }}</h2>
        <form @submit.prevent="save" class="correction-form">
          <div class="form-row">
            <label>Date
              <input v-model="form.date" type="date" required />
            </label>
            <label>User
              <select v-model.number="form.userId" required>
                <option disabled value="">Select a user</option>
                <option v-for="user in users" :key="user.id" :value="user.id">
                  {{ user.name }}{{ user.isActive ? '' : ' (inactive)' }}
                </option>
              </select>
            </label>
            <label class="description-field">Description
              <input v-model="form.description" type="text" maxlength="500" required />
            </label>
            <label>Amount incl. BTW (€)
              <input v-model="form.amount" type="number" step="0.01" required />
            </label>
            <div class="form-actions">
              <button type="submit" class="btn-primary" :disabled="saving">{{ editingId ? 'Update' : 'Add' }}</button>
              <button v-if="editingId" type="button" class="btn-secondary" @click="cancelEdit">Cancel</button>
            </div>
          </div>
          <p v-if="formError" class="error">{{ formError }}</p>
        </form>
      </div>

      <div class="table-card">
        <table v-if="corrections.length" class="corrections-table">
          <thead>
            <tr>
              <th>Date</th>
              <th>User</th>
              <th>Description</th>
              <th>Bedrag excl.</th>
              <th>BTW</th>
              <th>Totaal</th>
              <th>Invoice month</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in corrections" :key="item.id">
              <td>{{ item.date }}</td>
              <td>{{ item.userName }}</td>
              <td>{{ item.description }}</td>
              <td :class="amountClass(item.amount)">€{{ Number(item.amountExclVat).toFixed(2) }}</td>
              <td :class="amountClass(item.amount)">€{{ Number(item.vatAmount).toFixed(2) }}</td>
              <td :class="amountClass(item.amount)">€{{ Number(item.amount).toFixed(2) }}</td>
              <td>{{ monthLabel(item.date) }}</td>
              <td>
                <button class="btn-edit" type="button" @click="startEdit(item)">Edit</button>
                <button class="btn-danger" type="button" @click="remove(item)">Delete</button>
              </td>
            </tr>
          </tbody>
        </table>
        <p v-else class="no-data">No corrections yet.</p>
      </div>
    </div>
  </div>
</template>

<script>
import axios from 'axios'

const MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']

function todayInput() {
  const now = new Date()
  const local = new Date(now.getTime() - now.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 10)
}

function emptyForm() {
  return {
    date: todayInput(),
    userId: '',
    description: '',
    amount: ''
  }
}

export default {
  name: 'InvoiceCorrections',
  data() {
    return {
      corrections: [],
      users: [],
      loading: true,
      saving: false,
      error: null,
      formError: null,
      editingId: null,
      form: emptyForm()
    }
  },
  async mounted() {
    await this.load()
  },
  methods: {
    getToken() {
      return localStorage.getItem('token') || ''
    },
    authHeaders() {
      return { Authorization: `Bearer ${this.getToken()}` }
    },
    async load() {
      this.loading = true
      this.error = null
      try {
        const [corrections, users] = await Promise.all([
          axios.get('/api/invoicecorrections', { headers: this.authHeaders() }),
          axios.get('/api/auth/users', { headers: this.authHeaders() })
        ])
        this.corrections = corrections.data
        this.users = [...users.data].sort((a, b) => a.name.localeCompare(b.name))
      } catch (e) {
        this.error = 'Failed to load corrections.'
      } finally {
        this.loading = false
      }
    },
    startEdit(item) {
      this.editingId = item.id
      this.formError = null
      this.form = {
        date: String(item.date).slice(0, 10),
        userId: item.userId,
        description: item.description,
        amount: Number(item.amount)
      }
    },
    cancelEdit() {
      this.editingId = null
      this.formError = null
      this.form = emptyForm()
    },
    async save() {
      this.formError = null
      if (!this.form.userId) {
        this.formError = 'Select a user.'
        return
      }
      const amount = Number(this.form.amount)
      if (!Number.isFinite(amount) || amount === 0) {
        this.formError = 'Enter an amount other than zero.'
        return
      }
      const body = {
        userId: this.form.userId,
        date: this.form.date,
        description: this.form.description,
        amount
      }
      this.saving = true
      try {
        if (this.editingId) {
          await axios.put(`/api/invoicecorrections/${this.editingId}`, body, { headers: this.authHeaders() })
        } else {
          await axios.post('/api/invoicecorrections', body, { headers: this.authHeaders() })
        }
        this.cancelEdit()
        await this.load()
      } catch (e) {
        const data = e.response?.data
        this.formError = typeof data === 'string' ? data : 'Failed to save the correction.'
      } finally {
        this.saving = false
      }
    },
    async remove(item) {
      const label = `${item.userName} on ${String(item.date).slice(0, 10)} (€${Number(item.amount).toFixed(2)})`
      if (!confirm(`Delete the correction for ${label}?`)) return
      this.formError = null
      try {
        await axios.delete(`/api/invoicecorrections/${item.id}`, { headers: this.authHeaders() })
        if (this.editingId === item.id) this.cancelEdit()
        await this.load()
      } catch (e) {
        this.formError = 'Failed to delete the correction.'
      }
    },
    monthLabel(date) {
      const [year, month] = String(date).slice(0, 10).split('-')
      return `${MONTH_NAMES[Number(month) - 1]} ${year}`
    },
    amountClass(amount) {
      return Number(amount) < 0 ? 'amount-credit' : 'amount-charge'
    }
  }
}
</script>

<style scoped>
.corrections-page {
  padding: 24px;
  max-width: 980px;
  margin: 0 auto;
}

.page-header h1 {
  margin: 0 0 4px;
}

.subtitle {
  color: #6b7280;
  margin: 0 0 12px;
}

.back-link {
  color: #1d78be;
  font-weight: 600;
}

.form-card, .table-card {
  background: #fff;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  padding: 24px;
  margin-top: 24px;
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
}

.form-row select {
  min-width: 180px;
}

.description-field input {
  width: 280px;
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
.btn-primary:disabled { opacity: 0.6; cursor: default; }

.btn-secondary {
  padding: 8px 16px;
  background: #f3f4f6;
  color: #374151;
  border: 1px solid #d1d5db;
  border-radius: 6px;
  cursor: pointer;
  font-size: 0.9rem;
}

.corrections-table {
  width: 100%;
  border-collapse: collapse;
}

.corrections-table th {
  text-align: left;
  padding: 10px 12px;
  background: #f9fafb;
  border-bottom: 1px solid #e5e7eb;
  font-size: 0.85rem;
  color: #6b7280;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.corrections-table td {
  padding: 10px 12px;
  border-bottom: 1px solid #f3f4f6;
  font-size: 0.9rem;
}

.amount-charge { color: #166534; }
.amount-credit { color: #b45309; }

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
