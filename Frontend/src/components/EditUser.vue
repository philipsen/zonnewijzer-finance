<template>
  <div class="edit-user-page">
    here
    <div class="page-header">
      <div class="header-content">
        <router-link to="/users" class="back-link">
          ← Back to Users
        </router-link>
        <h1>Edit User</h1>
      </div>
    </div>

    <div v-if="loading" class="loading">
      Loading user data...
    </div>

    <div v-else-if="error" class="error">
      {{ error }}
    </div>

    <div v-else-if="user" class="edit-form-container">
      <div class="user-preview">
        <div class="preview-avatar">
          <img 
            v-if="user.picture" 
            :src="user.picture" 
            :alt="user.name"
            class="avatar-img"
          >
          <div v-else class="avatar-placeholder">
            {{ getUserInitials(form.name || user.name) }}
          </div>
        </div>
        <div class="preview-info">
          <h3>{{ form.name || user.name }}</h3>
          <p>{{ form.email || user.email }}</p>
        </div>
      </div>

      <form @submit.prevent="updateUser" class="edit-form">
        <div class="form-group">
          <label for="name" class="form-label">Full Name</label>
          <input
            type="text"
            id="name"
            v-model="form.name"
            :placeholder="user.name"
            class="form-input"
            required
          >
        </div>

        <div class="form-group">
          <label for="email" class="form-label">Email Address</label>
          <input
            type="email"
            id="email"
            v-model="form.email"
            :placeholder="user.email"
            class="form-input"
            required
          >
        </div>

        <div v-if="currentUserRole" class="form-group">
          <label for="role" class="form-label">Role</label>
          <select
            id="role"
            v-model="form.role"
            class="form-select"
          >
            <option value="Base">Base</option>
            <option value="Admin">Admin</option>
          </select>
        </div>

        <div v-else-if="user.role" class="form-group">
          <label class="form-label">Role</label>
          <div class="role-display">
            <span :class="['role-badge', 'role-' + user.role.toLowerCase()]">
              {{ user.role }}
            </span>
            <span class="role-note">You must be logged in to edit roles</span>
          </div>
        </div>

        <div class="user-metadata">
          <div class="metadata-item">
            <span class="metadata-label">Member since:</span>
            <span class="metadata-value">{{ formatDate(user.createdAt) }}</span>
          </div>
          <div class="metadata-item">
            <span class="metadata-label">Last login:</span>
            <span class="metadata-value">{{ formatDate(user.lastLoginAt) }}</span>
          </div>
        </div>

        <div class="form-actions">
          <button 
            type="button" 
            @click="cancelEdit"
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

export default {
  name: 'EditUser',
  setup() {
    const route = useRoute()
    const router = useRouter()
    
    const user = ref(null)
    const loading = ref(true)
    const saving = ref(false)
    const error = ref('')
    const currentUserRole = ref('')
    
    const form = ref({
      name: '',
      email: '',
      role: ''
    })

    const hasChanges = computed(() => {
      if (!user.value) return false
      const nameChanged = form.value.name !== user.value.name
      const emailChanged = form.value.email !== user.value.email
      const roleChanged = currentUserRole.value && form.value.role !== user.value.role
      return nameChanged || emailChanged || roleChanged
    })

    const fetchUser = async () => {
      try {
        loading.value = true
        const token = localStorage.getItem('token')
        
        if (!token) {
          error.value = 'You must be logged in to edit users'
          return
        }

        const userId = route.params.id
        
        // Get current user info to check role permissions
        const currentUserResponse = await axios.get('/api/auth/user', {
          headers: { Authorization: `Bearer ${token}` }
        })
        currentUserRole.value = currentUserResponse.data.role
        
        // Get user to edit
        const response = await axios.get(`/api/auth/users/${userId}`, {
          headers: { Authorization: `Bearer ${token}` }
        })
        
        user.value = response.data
        form.value = {
          name: response.data.name,
          email: response.data.email,
          role: response.data.role || 'Base'
        }
      } catch (err) {
        console.error('Failed to fetch user:', err)
        if (err.response?.status === 401) {
          error.value = 'Unauthorized. Please log in again.'
        } else if (err.response?.status === 404) {
          error.value = 'User not found.'
        } else {
          error.value = 'Failed to load user. Please try again later.'
        }
      } finally {
        loading.value = false
      }
    }

    const updateUser = async () => {
      if (!hasChanges.value) return
      
      try {
        saving.value = true
        const token = localStorage.getItem('token')
        const userId = route.params.id

        const updateData = {
          name: form.value.name,
          email: form.value.email
        }
        
        // Only include role if current user is authenticated
        if (currentUserRole.value) {
          updateData.role = form.value.role
        }
        
        const response = await axios.put(`/api/auth/users/${userId}`, updateData, {
          headers: { Authorization: `Bearer ${token}` }
        })
        
        user.value = response.data
        router.push('/users')
      } catch (err) {
        console.error('Failed to update user:', err)
        if (err.response?.status === 401) {
          error.value = 'Unauthorized. Please log in again.'
        } else {
          error.value = 'Failed to update user. Please try again.'
        }
      } finally {
        saving.value = false
      }
    }

    const cancelEdit = () => {
      router.push('/users')
    }

    const formatDate = (dateString) => {
      return new Date(dateString).toLocaleDateString('en-US', {
        year: 'numeric',
        month: 'long',
        day: 'numeric'
      })
    }

    const getUserInitials = (name) => {
      return name
        .split(' ')
        .map(part => part.charAt(0))
        .join('')
        .toUpperCase()
        .substring(0, 2)
    }

    onMounted(() => {
      fetchUser()
    })

    return {
      user,
      form,
      loading,
      saving,
      error,
      currentUserRole,
      hasChanges,
      updateUser,
      cancelEdit,
      formatDate,
      getUserInitials
    }
  }
}
</script>

<style scoped>
.edit-user-page {
  max-width: 600px;
  margin: 0 auto;
  padding: 2rem;
}

.page-header {
  margin-bottom: 2rem;
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
  transition: color 0.2s ease;
}

.back-link:hover {
  color: #0056b3;
}

.page-header h1 {
  margin: 0;
  color: #333;
}

.loading {
  text-align: center;
  padding: 2rem;
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

.edit-form-container {
  background: white;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  padding: 2rem;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.user-preview {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 1.5rem;
  background: #f8f9fa;
  border-radius: 8px;
  margin-bottom: 2rem;
}

.preview-avatar .avatar-img {
  width: 60px;
  height: 60px;
  border-radius: 50%;
  object-fit: cover;
  border: 2px solid #e9ecef;
}

.preview-avatar .avatar-placeholder {
  width: 60px;
  height: 60px;
  border-radius: 50%;
  background-color: #6c757d;
  color: white;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: bold;
  font-size: 1.2rem;
}

.preview-info h3 {
  margin: 0 0 0.25rem 0;
  color: #333;
}

.preview-info p {
  margin: 0;
  color: #666;
}

.edit-form {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.form-label {
  font-weight: 600;
  color: #333;
  font-size: 0.9rem;
}

.form-input {
  padding: 0.75rem;
  border: 1px solid #ced4da;
  border-radius: 4px;
  font-size: 1rem;
  transition: border-color 0.2s ease, box-shadow 0.2s ease;
}

.form-input:focus {
  outline: none;
  border-color: #007bff;
  box-shadow: 0 0 0 2px rgba(0, 123, 255, 0.25);
}

.form-select {
  padding: 0.75rem;
  border: 1px solid #ced4da;
  border-radius: 4px;
  font-size: 1rem;
  background-color: white;
  cursor: pointer;
  transition: border-color 0.2s ease, box-shadow 0.2s ease;
}

.form-select:focus {
  outline: none;
  border-color: #007bff;
  box-shadow: 0 0 0 2px rgba(0, 123, 255, 0.25);
}

.role-display {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.role-badge {
  padding: 0.25rem 0.75rem;
  border-radius: 15px;
  font-size: 0.8rem;
  font-weight: 600;
  text-transform: uppercase;
}

.role-admin {
  background-color: #dc3545;
  color: white;
}

.role-base {
  background-color: #6c757d;
  color: white;
}

.role-note {
  font-size: 0.8rem;
  color: #666;
  font-style: italic;
}

.user-metadata {
  padding: 1rem;
  background: #f8f9fa;
  border-radius: 6px;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.metadata-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.metadata-label {
  font-size: 0.9rem;
  color: #666;
  font-weight: 500;
}

.metadata-value {
  font-size: 0.9rem;
  color: #333;
}

.form-actions {
  display: flex;
  gap: 1rem;
  justify-content: flex-end;
  padding-top: 1rem;
  border-top: 1px solid #e9ecef;
}

.btn {
  padding: 0.75rem 1.5rem;
  border: none;
  border-radius: 4px;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
  text-decoration: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 120px;
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

@media (max-width: 768px) {
  .edit-user-page {
    padding: 1rem;
  }
  
  .edit-form-container {
    padding: 1.5rem;
  }
  
  .user-preview {
    flex-direction: column;
    text-align: center;
  }
  
  .form-actions {
    flex-direction: column;
  }
  
  .btn {
    width: 100%;
  }
}
</style>