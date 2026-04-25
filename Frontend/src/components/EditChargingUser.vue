<template>
  <div class="edit-charging-user-page">
    <div class="page-header">
      <div class="header-content">
        <router-link to="/charge-cards" class="back-link">
          ← Back to charge cards
        </router-link>
        <h1>Edit charge card {{ chargingUserId }}</h1>
        <p v-if="chargingUser?.value" class="rfid-line">
          RFID value: <code>{{ chargingUser.value }}</code>
        </p>
      </div>
    </div>

    <div v-if="loading" class="loading">Loading...</div>

    <div v-else-if="!isAdmin" class="access-denied">
      <h2>Access Denied</h2>
      <p>Only administrators can edit charge cards.</p>
    </div>

    <div v-else-if="error && !chargingUser" class="error">{{ error }}</div>

    <div v-else-if="chargingUser" class="edit-form-container">
      <div v-if="error" class="error">{{ error }}</div>

      <form @submit.prevent="save" class="edit-form">
        <div class="form-group">
          <label for="displayName" class="form-label">Display Name</label>
          <input
            type="text"
            id="displayName"
            v-model="form.displayName"
            placeholder="e.g. John Doe"
            class="form-input"
          >
          <p class="form-hint">Shown on invoices and lists. Synced from Smappee whitelist <em>comment</em> when present.</p>
        </div>

        <div class="form-group">
          <label for="linkedUserId" class="form-label">Linked App User</label>
          <select
            id="linkedUserId"
            v-model="form.linkedUserId"
            class="form-select"
          >
            <option :value="null">— Not linked</option>
            <option
              v-for="u in availableUsers"
              :key="u.id"
              :value="u.id"
            >
              <template v-if="u.chargeCardIds && u.chargeCardIds.length">
                {{ u.name }} (card(s) {{ u.chargeCardIds.join(', ') }})
              </template>
              <template v-else>
                {{ u.name }}
              </template>
            </option>
          </select>
          <p class="form-hint">Associates this charge card with an app account for invoicing.</p>
        </div>

        <fieldset class="address-fieldset">
          <legend class="fieldset-legend">Address</legend>

          <div class="form-row">
            <div class="form-group form-group-grow">
              <label for="street" class="form-label">Street</label>
              <input
                type="text"
                id="street"
                v-model="form.street"
                placeholder="e.g. Kerkstraat"
                class="form-input"
              >
            </div>
            <div class="form-group form-group-narrow">
              <label for="houseNumber" class="form-label">Number</label>
              <input
                type="text"
                id="houseNumber"
                v-model="form.houseNumber"
                placeholder="e.g. 12A"
                class="form-input"
              >
            </div>
          </div>

          <div class="form-row">
            <div class="form-group form-group-postal">
              <label for="postalCode" class="form-label">Postal code</label>
              <input
                type="text"
                id="postalCode"
                v-model="form.postalCode"
                placeholder="1234 AB"
                class="form-input"
                maxlength="7"
              >
            </div>
            <div class="form-group form-group-grow">
              <label for="placeName" class="form-label">Place</label>
              <input
                type="text"
                id="placeName"
                v-model="form.placeName"
                placeholder="e.g. Amsterdam"
                class="form-input"
              >
            </div>
          </div>
        </fieldset>

        <div class="form-actions">
          <button
            type="button"
            @click="cancel"
            class="btn btn-secondary"
            :disabled="saving"
          >
            Cancel
          </button>
          <button
            type="submit"
            class="btn btn-primary"
            :disabled="saving || !hasChanges"
          >
            {{ saving ? 'Saving...' : 'Save Changes' }}
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<script>
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import axios from 'axios'
import { isAppAdmin, roleFromToken } from '../utils/roles'

export default {
  name: 'EditChargingUser',
  setup() {
    const route = useRoute()
    const router = useRouter()

    const chargingUserId = parseInt(route.params.id)
    const chargingUser = ref(null)
    const availableUsers = ref([])
    const loading = ref(true)
    const saving = ref(false)
    const error = ref('')
    const isAdmin = ref(false)

    const token = localStorage.getItem('token')
    isAdmin.value = isAppAdmin(roleFromToken(token))

    const form = ref({
      displayName: '',
      linkedUserId: null,
      street: 'Warmoestuin',
      houseNumber: '',
      postalCode: '2286 XZ',
      placeName: 'Rijswijk'
    })

    const hasChanges = computed(() => {
      if (!chargingUser.value) return false
      const displayNameChanged = (form.value.displayName || null) !== (chargingUser.value.comment || null)
      const linkedUserChanged = form.value.linkedUserId !== (chargingUser.value.linkedUserId ?? null)
      const streetChanged = (form.value.street || null) !== (chargingUser.value.street || null)
      const houseNumberChanged = (form.value.houseNumber || null) !== (chargingUser.value.houseNumber || null)
      const postalCodeChanged = (form.value.postalCode || null) !== (chargingUser.value.postalCode || null)
      const placeNameChanged = (form.value.placeName || null) !== (chargingUser.value.placeName || null)
      return displayNameChanged || linkedUserChanged || streetChanged || houseNumberChanged || postalCodeChanged || placeNameChanged
    })

    const fetchData = async () => {
      if (!token) {
        error.value = 'You must be logged in.'
        loading.value = false
        return
      }
      try {
        loading.value = true
        const [cuRes, usersRes] = await Promise.all([
          axios.get(`/api/auth/charging-users/${chargingUserId}`, {
            headers: { Authorization: `Bearer ${token}` }
          }),
          axios.get('/api/auth/users', {
            headers: { Authorization: `Bearer ${token}` }
          })
        ])
        chargingUser.value = cuRes.data
        availableUsers.value = usersRes.data
        form.value = {
          displayName: cuRes.data.comment || '',
          linkedUserId: cuRes.data.linkedUserId ?? null,
          street: cuRes.data.street || 'Warmoestuin',
          houseNumber: cuRes.data.houseNumber || '',
          postalCode: cuRes.data.postalCode || '2286 XZ',
          placeName: cuRes.data.placeName || 'Rijswijk'
        }
      } catch (err) {
        error.value = err.response?.status === 404
          ? 'Charge card not found.'
          : 'Failed to load data. Please try again.'
      } finally {
        loading.value = false
      }
    }

    const save = async () => {
      if (!hasChanges.value) return
      try {
        saving.value = true
        error.value = ''
        const payload = {
          displayName: form.value.displayName || null,
          linkedUserId: form.value.linkedUserId,
          unlinkUser: form.value.linkedUserId === null,
          street: form.value.street || null,
          houseNumber: form.value.houseNumber || null,
          postalCode: form.value.postalCode || null,
          placeName: form.value.placeName || null
        }
        await axios.put(`/api/auth/charging-users/${chargingUserId}`, payload, {
          headers: { Authorization: `Bearer ${token}` }
        })
        router.push('/charge-cards')
      } catch (err) {
        error.value = err.response?.data || 'Failed to save. Please try again.'
      } finally {
        saving.value = false
      }
    }

    const cancel = () => {
      router.push('/charge-cards')
    }

    onMounted(fetchData)

    return {
      chargingUserId,
      chargingUser,
      availableUsers,
      loading,
      saving,
      error,
      isAdmin,
      form,
      hasChanges,
      save,
      cancel
    }
  }
}
</script>

<style scoped>
.edit-charging-user-page {
  max-width: 600px;
  margin: 0 auto;
  padding: 2rem;
}

.page-header {
  margin-bottom: 2rem;
}

.rfid-line {
  margin: 0.35rem 0 0 0;
  font-size: 0.95rem;
  color: #555;
}

.rfid-line code {
  font-size: 0.9rem;
  background: #f4f4f4;
  padding: 0.15rem 0.4rem;
  border-radius: 4px;
}

.header-content {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.back-link {
  color: #007bff;
  text-decoration: none;
  font-size: 0.9rem;
}

.back-link:hover {
  text-decoration: underline;
}

.access-denied {
  text-align: center;
  padding: 3rem;
  color: #721c24;
  background-color: #f8d7da;
  border: 1px solid #f5c6cb;
  border-radius: 8px;
}

.edit-form-container {
  background: white;
  border: 1px solid #e0e0e0;
  border-radius: 8px;
  padding: 2rem;
}

.edit-form {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.form-label {
  font-weight: 600;
  font-size: 0.9rem;
  color: #333;
}

.form-input,
.form-select {
  padding: 0.6rem 0.8rem;
  border: 1px solid #ccc;
  border-radius: 6px;
  font-size: 1rem;
  width: 100%;
  box-sizing: border-box;
}

.form-input:focus,
.form-select:focus {
  outline: none;
  border-color: #007bff;
  box-shadow: 0 0 0 2px rgba(0, 123, 255, 0.15);
}

.form-hint {
  font-size: 0.8rem;
  color: #888;
  margin: 0;
}

.address-fieldset {
  border: 1px solid #e0e0e0;
  border-radius: 6px;
  padding: 1rem 1.25rem;
  margin: 0;
}

.fieldset-legend {
  font-weight: 600;
  font-size: 0.9rem;
  color: #333;
  padding: 0 0.4rem;
}

.form-row {
  display: flex;
  gap: 1rem;
}

.form-group-grow {
  flex: 1;
}

.form-group-narrow {
  flex: 0 0 100px;
}

.form-group-postal {
  flex: 0 0 130px;
}

.form-actions {
  display: flex;
  gap: 1rem;
  justify-content: flex-end;
}

.btn {
  padding: 0.6rem 1.4rem;
  border: none;
  border-radius: 6px;
  font-size: 0.95rem;
  cursor: pointer;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-primary {
  background: #007bff;
  color: white;
}

.btn-primary:hover:not(:disabled) {
  background: #0056b3;
}

.btn-secondary {
  background: #f0f0f0;
  color: #333;
}

.btn-secondary:hover:not(:disabled) {
  background: #e0e0e0;
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
</style>
