<template>
  <div class="charging-users-page">
    <div class="page-header">
      <h1>Charge cards</h1>
      <p class="subtitle">Cards come from Smappee whitelist sync and session RFIDs. The comment is the display label when present.</p>
    </div>

    <div v-if="loading" class="loading">Loading...</div>
    <div v-else-if="error" class="error">{{ error }}</div>

    <div v-else>
      <div class="table-card">
        <table v-if="chargingUsers.length" class="charging-users-table">
          <thead>
            <tr>
              <th>Value (RFID)</th>
              <th>Comment</th>
              <th>Linked app user</th>
              <th v-if="isAdmin">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="cu in chargingUsers" :key="cu.id">
              <td class="mono-cell">{{ cu.value || '—' }}</td>
              <td>{{ cu.comment || '—' }}</td>
              <td>
                <span v-if="cu.linkedUserName" class="linked-badge">{{ cu.linkedUserName }}</span>
                <span v-else class="unlinked-badge">Not linked</span>
              </td>
              <td v-if="isAdmin">
                <router-link :to="`/charge-cards/${cu.id}/edit`" class="btn-edit">Edit</router-link>
              </td>
            </tr>
          </tbody>
        </table>
        <p v-else class="no-data">No charge cards yet. Run a Smappee sync from Charging Sessions.</p>
      </div>
    </div>
  </div>
</template>

<script>
import axios from 'axios'
import { isAppAdmin, roleFromToken } from '../utils/roles'

export default {
  name: 'ChargingUsersList',
  data() {
    return {
      chargingUsers: [],
      loading: true,
      error: null,
      isAdmin: false
    }
  },
  async mounted() {
    this.isAdmin = isAppAdmin(roleFromToken(localStorage.getItem('token')))
    await this.loadChargingUsers()
  },
  methods: {
    getToken() {
      return localStorage.getItem('token') || ''
    },
    async loadChargingUsers() {
      this.loading = true
      this.error = null
      try {
        const { data } = await axios.get('/api/auth/charging-users', {
          headers: { Authorization: `Bearer ${this.getToken()}` }
        })
        this.chargingUsers = data
      } catch {
        this.error = 'Failed to load charge cards.'
      } finally {
        this.loading = false
      }
    }
  }
}
</script>

<style scoped>
.mono-cell {
  font-family: ui-monospace, monospace;
  font-size: 0.85rem;
  color: #444;
}

.charging-users-page {
  max-width: 900px;
  margin: 0 auto;
  padding: 2rem;
}

.page-header {
  margin-bottom: 2rem;
}

.page-header h1 {
  margin: 0 0 0.5rem 0;
}

.subtitle {
  color: #666;
  margin: 0;
}

.table-card {
  background: white;
  border: 1px solid #e0e0e0;
  border-radius: 8px;
  overflow: hidden;
}

.charging-users-table {
  width: 100%;
  border-collapse: collapse;
}

.charging-users-table th,
.charging-users-table td {
  padding: 0.75rem 1rem;
  text-align: left;
  border-bottom: 1px solid #e0e0e0;
}

.charging-users-table th {
  background: #f5f5f5;
  font-weight: 600;
  color: #333;
}

.charging-users-table tr:last-child td {
  border-bottom: none;
}

.charging-users-table tr:hover td {
  background: #fafafa;
}

.linked-badge {
  background: #d4edda;
  color: #155724;
  padding: 0.2rem 0.6rem;
  border-radius: 12px;
  font-size: 0.85rem;
}

.unlinked-badge {
  color: #999;
  font-size: 0.85rem;
}

.btn-edit {
  background: #007bff;
  color: white;
  border: none;
  padding: 0.3rem 0.75rem;
  border-radius: 4px;
  font-size: 0.85rem;
  cursor: pointer;
  text-decoration: none;
  display: inline-block;
}

.btn-edit:hover {
  background: #0056b3;
}

.loading {
  padding: 2rem;
  text-align: center;
  color: #666;
}

.error {
  padding: 1rem;
  background: #f8d7da;
  color: #721c24;
  border-radius: 8px;
  margin-bottom: 1rem;
}

.no-data {
  padding: 2rem;
  text-align: center;
  color: #666;
}
</style>
