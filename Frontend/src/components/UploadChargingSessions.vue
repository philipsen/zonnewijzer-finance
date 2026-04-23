<template>
  <div class="upload-page">
    <div v-if="!isAdmin" class="access-denied">
      <h2>Access Denied</h2>
      <p>Only administrators can manage charging data.</p>
    </div>
    <template v-else>
      <div class="page-header">
        <h1>Charging data</h1>
        <p class="page-description">
          Sessions and whitelisted charge cards are loaded from <strong>Smappee Cloud</strong> on the
          <router-link to="/charging-sessions">Charging Sessions</router-link> page (admin: <strong>Sync from Smappee</strong>).
          Use the button below only if you need to wipe sessions and charge cards in this app while keeping invoices and payments.
        </p>
        <div class="page-toolbar">
          <button type="button" class="btn btn-clear-charging" @click="openClearModal">
            Clear charging sessions &amp; charge cards…
          </button>
        </div>
      </div>

      <Teleport to="body">
        <div v-if="clearModalOpen" class="modal-backdrop" @click.self="closeClearModal">
          <div class="modal-panel" role="dialog" aria-modal="true" aria-labelledby="clear-modal-title">
            <h2 id="clear-modal-title" class="modal-title">Clear charging data?</h2>
            <p class="modal-warning">
              This removes <strong>all</strong> charging sessions and charge cards, and unlinks every app user from a charge card.
              Invoices and payments in the database are <strong>not</strong> deleted.
            </p>
            <p class="modal-instructions">Type exactly (case-sensitive):</p>
            <code class="modal-phrase">{{ clearConfirmationPhrase }}</code>
            <input
              v-model="clearConfirmInput"
              type="text"
              class="modal-input"
              autocomplete="off"
              placeholder="Confirmation phrase"
              @keydown.esc="closeClearModal"
            >
            <p v-if="clearError" class="modal-error">{{ clearError }}</p>
            <div class="modal-actions">
              <button type="button" class="btn-cancel" :disabled="clearLoading" @click="closeClearModal">Cancel</button>
              <button
                type="button"
                class="btn-confirm-clear"
                :disabled="clearLoading || clearConfirmInput !== clearConfirmationPhrase"
                @click="executeClearCharging"
              >
                {{ clearLoading ? 'Clearing…' : 'Clear charging data' }}
              </button>
            </div>
          </div>
        </div>
      </Teleport>
    </template>
  </div>
</template>

<script>
import { ref } from 'vue'
import axios from 'axios'
import { isAppAdmin, roleFromToken } from '../utils/roles'

/** Must match Backend AuthController.ClearChargingDataConfirmationPhrase */
const CLEAR_CHARGING_CONFIRMATION_PHRASE = 'CLEAR CHARGING DATA'

export default {
  name: 'UploadChargingSessions',
  setup() {
    const isAdmin = ref(isAppAdmin(roleFromToken(localStorage.getItem('token'))))

    const clearModalOpen = ref(false)
    const clearConfirmInput = ref('')
    const clearLoading = ref(false)
    const clearError = ref('')
    const clearConfirmationPhrase = CLEAR_CHARGING_CONFIRMATION_PHRASE

    const openClearModal = () => {
      clearError.value = ''
      clearConfirmInput.value = ''
      clearModalOpen.value = true
    }

    const closeClearModal = () => {
      if (clearLoading.value) return
      clearModalOpen.value = false
      clearConfirmInput.value = ''
      clearError.value = ''
    }

    const executeClearCharging = async () => {
      if (clearConfirmInput.value !== CLEAR_CHARGING_CONFIRMATION_PHRASE) return
      clearError.value = ''
      clearLoading.value = true
      try {
        const token = localStorage.getItem('token')
        await axios.post(
          '/api/auth/admin/clear-charging-data',
          { confirmation: CLEAR_CHARGING_CONFIRMATION_PHRASE },
          { headers: { Authorization: `Bearer ${token}` } }
        )
        closeClearModal()
        window.location.reload()
      } catch (err) {
        console.error('Clear charging data failed:', err)
        if (err.response?.status === 403) {
          clearError.value = 'Only administrators can clear charging data.'
        } else if (err.response?.data?.message) {
          clearError.value = err.response.data.message
        } else {
          clearError.value = 'Request failed. Please try again.'
        }
      } finally {
        clearLoading.value = false
      }
    }

    return {
      isAdmin,
      clearModalOpen,
      clearConfirmInput,
      clearLoading,
      clearError,
      clearConfirmationPhrase,
      openClearModal,
      closeClearModal,
      executeClearCharging
    }
  }
}
</script>

<style scoped>
.upload-page {
  max-width: 800px;
  margin: 0 auto;
  padding: 2rem;
}

.access-denied {
  text-align: center;
  padding: 3rem;
  color: #721c24;
  background-color: #f8d7da;
  border: 1px solid #f5c6cb;
  border-radius: 8px;
}

.access-denied h2 {
  margin: 0 0 0.5rem 0;
}

.page-header {
  text-align: center;
  margin-bottom: 2rem;
}

.page-header h1 {
  margin: 0 0 0.5rem 0;
  color: #333;
}

.page-description {
  color: #666;
  font-size: 1.05rem;
  margin: 0;
  line-height: 1.5;
}

.page-description a {
  color: #0066cc;
  font-weight: 600;
}

.page-toolbar {
  margin-top: 1.25rem;
}

.btn-clear-charging {
  background: #fff;
  color: #842029;
  border: 1px solid #f5c2c7;
  padding: 0.45rem 0.85rem;
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-clear-charging:hover {
  background: #f8d7da;
}

.modal-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 2000;
  padding: 1rem;
}

.modal-panel {
  background: #fff;
  border-radius: 10px;
  max-width: 420px;
  width: 100%;
  padding: 1.5rem;
  box-shadow: 0 12px 40px rgba(0, 0, 0, 0.2);
}

.modal-title {
  margin: 0 0 0.75rem 0;
  font-size: 1.2rem;
  color: #333;
}

.modal-warning {
  color: #555;
  font-size: 0.95rem;
  line-height: 1.45;
  margin: 0 0 1rem 0;
}

.modal-instructions {
  margin: 0 0 0.35rem 0;
  font-size: 0.9rem;
  color: #333;
}

.modal-phrase {
  display: block;
  background: #f1f3f5;
  padding: 0.5rem 0.75rem;
  border-radius: 6px;
  font-size: 0.9rem;
  margin-bottom: 0.75rem;
  word-break: break-all;
}

.modal-input {
  width: 100%;
  box-sizing: border-box;
  padding: 0.5rem 0.75rem;
  border: 1px solid #ced4da;
  border-radius: 6px;
  font-size: 0.95rem;
  margin-bottom: 0.5rem;
}

.modal-error {
  color: #842029;
  font-size: 0.9rem;
  margin: 0 0 0.5rem 0;
}

.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 0.75rem;
  margin-top: 1rem;
}

.btn-cancel {
  background: #e9ecef;
  border: none;
  padding: 0.5rem 1rem;
  border-radius: 6px;
  cursor: pointer;
  font-weight: 500;
}

.btn-cancel:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-confirm-clear {
  background: #842029;
  color: #fff;
  border: none;
  padding: 0.5rem 1rem;
  border-radius: 6px;
  cursor: pointer;
  font-weight: 600;
}

.btn-confirm-clear:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
