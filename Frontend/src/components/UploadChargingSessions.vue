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
          Upload CSV files containing charging session data. Duplicate sessions will be automatically skipped.
        </p>
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
          <div class="stat">
            <span class="stat-number">{{ uploadResult.data.skipped }}</span>
            <span class="stat-label">Duplicates Skipped</span>
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
      <h3>CSV Format Requirements</h3>
      <div class="format-example">
        <pre>start,end,userid,stationid,charged_kwh
2024-01-15T08:15:00,2024-01-15T10:30:00,1,3,45.2
2024-01-15T09:45:00,2024-01-15T11:20:00,3,1,32.8</pre>
      </div>
      <ul class="format-rules">
        <li><strong>start/end:</strong> ISO datetime format (YYYY-MM-DDTHH:MM:SS)</li>
        <li><strong>userid:</strong> Integer (1-5)</li>
        <li><strong>stationid:</strong> Integer (1-5)</li>
        <li><strong>charged_kwh:</strong> Decimal number for energy delivered</li>
      </ul>
    </div>
    </template>
  </div>
</template>

<script>
import { ref } from 'vue'
import axios from 'axios'

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
      uploadFile
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