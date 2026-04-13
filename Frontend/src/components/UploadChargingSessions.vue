<template>
  <div class="upload-page">
    <div v-if="!isAdmin" class="access-denied">
      <h2>Access Denied</h2>
      <p>Only administrators can upload charging sessions.</p>
    </div>
    <template v-else>
      <div class="page-header">
        <h1>Upload Charging Sessions</h1>
        <p class="page-description">
          Upload a charging station <strong>CSV export</strong> with columns
          <strong>Station serial number</strong>, <strong>Charging station name</strong>, <strong>From</strong>, <strong>To</strong>,
          <strong>Duration [h:mm]</strong>, <strong>kWh</strong> (comma decimals, e.g. <code>0,388</code>), <strong>Connector</strong>, <strong>Token</strong>.
          A <strong>charging point</strong> is identified by <strong>station serial + connector</strong>; the station name is stored and shown in session lists and invoices.
          The RFID segment before <code> (</code> in the token column (typically hex) is stored as the profile <strong>RFID key</strong> and mapped to a numeric <strong>charging user id</strong> (hex value modulo 2<sup>31</sup>−1; non-hex tokens use a hash fallback).
          Text between <code> (</code> and <code>)</code> becomes the charging user <strong>display name</strong> when the profile is first created on import.
          For a clean import you can <strong>clear charging data only</strong> (sessions + charging user profiles + app links) with the button below, or use <strong>Wipe all data</strong> on Home for a broader reset.
          Rows that repeat the same <strong>start time</strong>, <strong>charging user</strong>, and <strong>charging point</strong> (station serial + connector) as an earlier row in the file are skipped. Sample: <code>Backend/Assets/sample-charging-export.csv</code> (20 sessions, 3 tokens).
        </p>
        <div class="page-toolbar">
          <button type="button" class="btn btn-clear-charging" @click="openClearModal">
            Clear charging sessions &amp; profiles…
          </button>
        </div>
      </div>

    <div class="upload-container">
      <div class="upload-form">
        <div class="file-upload-area" 
             :class="{ 'drag-over': isDragOver, 'uploading': uploading }"
             @dragenter.prevent="handleDragEnter"
             @dragover.prevent="handleDragOver"
             @dragleave.prevent="handleDragLeave"
             @drop.prevent="handleDrop"
             @click="triggerFileInput">
          
          <input 
            ref="fileInput" 
            type="file" 
            accept=".csv" 
            @change="handleFileSelect"
            style="display: none;"
          >
          
          <div v-if="!uploading" class="upload-content">
            <div class="upload-icon">📄</div>
            <h3>Drop CSV file here or click to browse</h3>
            <p>Supports .csv files only</p>
          </div>
          
          <div v-else class="uploading-content">
            <div class="spinner"></div>
            <p>Uploading and processing...</p>
          </div>
        </div>

        <div v-if="selectedFile" class="selected-file">
          <strong>Selected file:</strong> {{ selectedFile.name }}
          <button @click="clearFile" class="clear-btn">×</button>
        </div>

        <div class="upload-actions">
          <button 
            @click="uploadFile" 
            :disabled="!selectedFile || uploading"
            class="btn btn-primary"
          >
            {{ uploading ? 'Uploading...' : 'Upload CSV' }}
          </button>
        </div>
      </div>

      <div v-if="uploadResult" class="upload-result" :class="uploadResult.success ? 'success' : 'error'">
        <h3>{{ uploadResult.success ? 'Upload Successful!' : 'Upload Failed' }}</h3>
        <div v-if="uploadResult.success" class="result-stats">
          <div class="stat">
            <span class="stat-number">{{ uploadResult.data.added }}</span>
            <span class="stat-label">Sessions Added</span>
          </div>
          <div class="stat" v-if="(uploadResult.data.updated ?? 0) > 0">
            <span class="stat-number">{{ uploadResult.data.updated }}</span>
            <span class="stat-label">Existing Sessions Updated</span>
          </div>
          <div class="stat">
            <span class="stat-number">{{ uploadResult.data.skipped }}</span>
            <span class="stat-label">Duplicate start / user / station in file</span>
          </div>
          <div class="stat" v-if="uploadResult.data.errors > 0">
            <span class="stat-number">{{ uploadResult.data.errors }}</span>
            <span class="stat-label">Errors</span>
          </div>
          <div class="stat">
            <span class="stat-number">{{ uploadResult.data.total }}</span>
            <span class="stat-label">Total Processed</span>
          </div>
        </div>
        <p v-else class="error-message">{{ uploadResult.message }}</p>
      </div>
    </div>

    <div class="csv-format-info">
      <h3>CSV format (portal export)</h3>
      <p class="format-intro">
        The server only accepts a <strong>charging station portal export</strong>: the first non-empty line of the file must contain the text
        <code>Station serial number</code> (see repo file <code>Backend/Assets/sample-charging-export.csv</code>). Column headers are matched by name (spaces matter).
      </p>
      <div class="format-example">
        <pre>Station serial number,Charging station name,From,To,Duration [h:mm],kWh,Connector,Token
6531001272,Paal 2,2026-04-01 07:12:00,2026-04-01 07:28:14,00:16,"2,150",1,047D9322B62290 (Cock van Bruggen)</pre>
      </div>
      <ul class="format-rules">
        <li><strong>Used for import:</strong> <em>Station serial number</em>, <em>Charging station name</em>, <em>Connector</em>, <em>From</em>, <em>To</em>, <em>kWh</em>, <em>Token</em>. <em>Duration</em> is not stored. The internal charging-point id is derived from <strong>serial + connector</strong> (same pair → same point).</li>
        <li><strong>From / To:</strong> date-time strings the server can parse (export uses <code>yyyy-MM-dd HH:mm:ss</code>).</li>
        <li><strong>kWh:</strong> comma decimal (Dutch, e.g. <code>"2,150"</code>) or dot decimal; quotes optional.</li>
        <li><strong>Token:</strong> required; text before <code> (</code> is the RFID key (hex → charging user id; see upload description). Text inside <code>( … )</code> is saved as the charging user display name for new profiles.</li>
        <li><strong>De-duplication:</strong> within one file, same <em>From</em> + same derived user + same charging point (serial + connector) as a previous row → skipped (counted as duplicate).</li>
        <li><strong>Re-import:</strong> if the database already has a session with the same <em>From</em>, user, and charging point, that row is <strong>updated</strong> with the new CSV values (kWh, end time, station name, etc.).</li>
      </ul>
    </div>

    <Teleport to="body">
      <div v-if="clearModalOpen" class="modal-backdrop" @click.self="closeClearModal">
        <div class="modal-panel" role="dialog" aria-modal="true" aria-labelledby="clear-modal-title">
          <h2 id="clear-modal-title" class="modal-title">Clear charging data?</h2>
          <p class="modal-warning">
            This removes <strong>all</strong> charging sessions and charging user profiles, and unlinks every app user from a charging account.
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

/** Must match Backend AuthController.ClearChargingDataConfirmationPhrase */
const CLEAR_CHARGING_CONFIRMATION_PHRASE = 'CLEAR CHARGING DATA'

export default {
  name: 'UploadChargingSessions',
  setup() {
    const selectedFile = ref(null)
    const uploading = ref(false)
    const uploadResult = ref(null)
    const isDragOver = ref(false)
    const fileInput = ref(null)

    // Decode role from JWT to guard the UI
    const isAdmin = ref(false)
    const token = localStorage.getItem('token')
    if (token) {
      try {
        const payload = JSON.parse(atob(token.split('.')[1]))
        const role = payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
        isAdmin.value = role === 'Admin'
      } catch {
        isAdmin.value = false
      }
    }

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
        clearLoading.value = false
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

    const triggerFileInput = () => {
      fileInput.value.click()
    }

    const handleFileSelect = (event) => {
      const file = event.target.files[0]
      if (file) {
        selectedFile.value = file
        uploadResult.value = null
      }
    }

    const handleDragEnter = () => {
      isDragOver.value = true
    }

    const handleDragOver = () => {
      isDragOver.value = true
    }

    const handleDragLeave = (event) => {
      if (!event.currentTarget.contains(event.relatedTarget)) {
        isDragOver.value = false
      }
    }

    const handleDrop = (event) => {
      isDragOver.value = false
      const files = event.dataTransfer.files
      if (files.length > 0 && files[0].name.endsWith('.csv')) {
        selectedFile.value = files[0]
        uploadResult.value = null
      }
    }

    const clearFile = () => {
      selectedFile.value = null
      fileInput.value.value = ''
      uploadResult.value = null
    }

    const uploadFile = async () => {
      if (!selectedFile.value) return

      try {
        uploading.value = true
        uploadResult.value = null

        const formData = new FormData()
        formData.append('file', selectedFile.value)

        const token = localStorage.getItem('token')
        const response = await axios.post('/api/chargingsessions/upload', formData, {
          headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'multipart/form-data'
          }
        })

        uploadResult.value = {
          success: true,
          data: response.data
        }

        // Clear file after successful upload
        setTimeout(() => {
          clearFile()
        }, 2000)

      } catch (error) {
        console.error('Upload failed:', error)
        uploadResult.value = {
          success: false,
          message: error.response?.data?.message || 'Upload failed. Please try again.'
        }
      } finally {
        uploading.value = false
      }
    }

    return {
      isAdmin,
      selectedFile,
      uploading,
      uploadResult,
      isDragOver,
      fileInput,
      triggerFileInput,
      handleFileSelect,
      handleDragEnter,
      handleDragOver,
      handleDragLeave,
      handleDrop,
      clearFile,
      uploadFile,
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
  font-size: 1.1rem;
  margin: 0;
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
  z-index: 10000;
  padding: 1rem;
}

.modal-panel {
  background: #fff;
  max-width: 32rem;
  width: 100%;
  border-radius: 10px;
  padding: 1.5rem;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.2);
}

.modal-title {
  margin: 0 0 0.75rem 0;
  color: #842029;
  font-size: 1.25rem;
}

.modal-warning {
  margin: 0 0 1rem 0;
  color: #333;
  line-height: 1.5;
  font-size: 0.95rem;
}

.modal-instructions {
  margin: 0 0 0.35rem 0;
  font-size: 0.9rem;
  color: #555;
}

.modal-phrase {
  display: block;
  background: #f8f9fa;
  padding: 0.5rem 0.75rem;
  border-radius: 6px;
  font-size: 0.9rem;
  margin-bottom: 0.75rem;
  word-break: break-all;
}

.modal-input {
  width: 100%;
  box-sizing: border-box;
  padding: 0.6rem 0.75rem;
  border: 1px solid #ced4da;
  border-radius: 6px;
  font-size: 1rem;
}

.modal-input:focus {
  outline: none;
  border-color: #842029;
  box-shadow: 0 0 0 2px rgba(132, 32, 41, 0.15);
}

.modal-error {
  color: #842029;
  font-size: 0.9rem;
  margin: 0.5rem 0 0 0;
}

.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  margin-top: 1.25rem;
}

.btn-cancel {
  background: #e9ecef;
  color: #333;
  border: none;
  padding: 0.5rem 1rem;
  border-radius: 6px;
  font-weight: 600;
  cursor: pointer;
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
  font-weight: 600;
  cursor: pointer;
}

.btn-confirm-clear:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-confirm-clear:not(:disabled):hover {
  background: #6a1a22;
}

.upload-container {
  background: white;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  padding: 2rem;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
  margin-bottom: 2rem;
}

.file-upload-area {
  border: 2px dashed #dee2e6;
  border-radius: 8px;
  padding: 3rem 2rem;
  text-align: center;
  cursor: pointer;
  transition: all 0.3s ease;
  margin-bottom: 1.5rem;
}

.file-upload-area:hover {
  border-color: #007bff;
  background-color: #f8f9fa;
}

.file-upload-area.drag-over {
  border-color: #007bff;
  background-color: #e3f2fd;
}

.file-upload-area.uploading {
  pointer-events: none;
  opacity: 0.7;
}

.upload-icon {
  font-size: 3rem;
  margin-bottom: 1rem;
}

.upload-content h3 {
  margin: 0 0 0.5rem 0;
  color: #333;
}

.upload-content p {
  margin: 0;
  color: #666;
}

.uploading-content {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
}

.spinner {
  width: 40px;
  height: 40px;
  border: 4px solid #f3f3f3;
  border-top: 4px solid #007bff;
  border-radius: 50%;
  animation: spin 1s linear infinite;
}

@keyframes spin {
  0% { transform: rotate(0deg); }
  100% { transform: rotate(360deg); }
}

.selected-file {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: #f8f9fa;
  padding: 1rem;
  border-radius: 4px;
  margin-bottom: 1.5rem;
}

.clear-btn {
  background: none;
  border: none;
  font-size: 1.2rem;
  cursor: pointer;
  color: #666;
  padding: 0.25rem;
}

.upload-actions {
  text-align: center;
}

.btn {
  padding: 0.75rem 2rem;
  border: none;
  border-radius: 4px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
  min-width: 150px;
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

.upload-result {
  margin-top: 2rem;
  padding: 1.5rem;
  border-radius: 8px;
}

.upload-result.success {
  background-color: #d4edda;
  border: 1px solid #c3e6cb;
  color: #155724;
}

.upload-result.error {
  background-color: #f8d7da;
  border: 1px solid #f5c6cb;
  color: #721c24;
}

.upload-result h3 {
  margin: 0 0 1rem 0;
}

.result-stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: 1rem;
}

.stat {
  text-align: center;
}

.stat-number {
  display: block;
  font-size: 2rem;
  font-weight: bold;
  color: #155724;
}

.stat-label {
  display: block;
  font-size: 0.9rem;
  color: #155724;
}

.csv-format-info {
  background: #f8f9fa;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  padding: 1.5rem;
}

.csv-format-info h3 {
  margin: 0 0 1rem 0;
  color: #333;
}

.format-intro {
  margin: 0 0 1rem 0;
  color: #555;
  font-size: 0.95rem;
  line-height: 1.5;
}

.format-example {
  background: white;
  border: 1px solid #dee2e6;
  border-radius: 4px;
  margin-bottom: 1rem;
}

.format-example pre {
  margin: 0;
  padding: 1rem;
  font-family: monospace;
  font-size: 0.9rem;
  overflow-x: auto;
}

.format-rules {
  margin: 0;
  padding-left: 1.5rem;
}

.format-rules li {
  margin-bottom: 0.5rem;
  color: #555;
}

@media (max-width: 768px) {
  .upload-page {
    padding: 0.75rem;
  }

  .page-header h1 {
    font-size: 1.4rem;
  }

  .upload-container {
    padding: 1rem;
  }

  .file-upload-area {
    padding: 1.5rem 1rem;
  }

  .upload-icon {
    font-size: 2rem;
  }

  .upload-content h3 {
    font-size: 1rem;
  }

  .result-stats {
    grid-template-columns: repeat(2, 1fr);
  }

  .stat-number {
    font-size: 1.5rem;
  }

  .format-example pre {
    font-size: 0.75rem;
  }

  .csv-format-info {
    padding: 1rem;
  }
}
</style>