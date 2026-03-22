<template>
  <div class="users-page">
    <div class="page-header">
      <h1>All Users</h1>
      <div class="user-count" v-if="users.length">
        {{ users.length }} user{{ users.length !== 1 ? 's' : '' }} registered
      </div>
    </div>
    
    <div v-if="loading" class="loading">
      Loading users...
    </div>
    
    <div v-else-if="error" class="error">
      {{ error }}
    </div>
    
    <div v-else-if="users.length === 0" class="no-users">
      No users found.
    </div>
    
    <div v-else class="users-grid">
      <div v-for="user in users" :key="user.id" class="user-card">
        <div class="user-avatar">
          <img 
            v-if="user.picture" 
            :src="user.picture" 
            :alt="user.name"
            class="avatar-img"
          >
          <div v-else class="avatar-placeholder">
            {{ getUserInitials(user.name) }}
          </div>
        </div>
        <div class="user-info">
          <h3 class="user-name">{{ user.name }}</h3>
          <p class="user-email">{{ user.email }}</p>
          <div class="user-dates">
            <div class="date-item">
              <span class="date-label">Joined:</span>
              <span class="date-value">{{ formatDate(user.createdAt) }}</span>
            </div>
            <div class="date-item">
              <span class="date-label">Last Login:</span>
              <span class="date-value">{{ formatDate(user.lastLoginAt) }}</span>
            </div>
          </div>
        </div>
        <div class="user-actions">
          <router-link 
            :to="`/users/${user.id}/edit`" 
            class="btn btn-edit"
          >
            Edit User
          </router-link>
        </div>
      </div>
    </div>
  </div>
</template>

<script>
import { ref, onMounted } from 'vue'
import axios from 'axios'

export default {
  name: 'UsersList',
  setup() {
    const users = ref([])
    const loading = ref(true)
    const error = ref('')

    const fetchUsers = async () => {
      try {
        loading.value = true
        const token = localStorage.getItem('token')
        
        if (!token) {
          error.value = 'You must be logged in to view users'
          return
        }

        const response = await axios.get('/api/auth/users', {
          headers: { Authorization: `Bearer ${token}` }
        })
        
        users.value = response.data
      } catch (err) {
        console.error('Failed to fetch users:', err)
        if (err.response?.status === 401) {
          error.value = 'Unauthorized. Please log in again.'
        } else {
          error.value = 'Failed to load users. Please try again later.'
        }
      } finally {
        loading.value = false
      }
    }

    const formatDate = (dateString) => {
      return new Date(dateString).toLocaleDateString('en-US', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
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
      fetchUsers()
    })

    return {
      users,
      loading,
      error,
      formatDate,
      getUserInitials
    }
  }
}
</script>

<style scoped>
.users-page {
  max-width: 1200px;
  margin: 0 auto;
  padding: 2rem;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 2rem;
  border-bottom: 2px solid #e9ecef;
  padding-bottom: 1rem;
}

.page-header h1 {
  margin: 0;
  color: #333;
}

.user-count {
  color: #666;
  font-size: 1rem;
  background: #f8f9fa;
  padding: 0.5rem 1rem;
  border-radius: 20px;
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

.no-users {
  text-align: center;
  padding: 2rem;
  color: #666;
  font-size: 1.1rem;
}

.users-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(350px, 1fr));
  gap: 1.5rem;
}

.user-card {
  background: white;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  padding: 1.5rem;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
  transition: transform 0.2s ease, box-shadow 0.2s ease;
}

.user-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 8px rgba(0, 0, 0, 0.15);
}

.user-avatar {
  display: flex;
  justify-content: center;
  margin-bottom: 1rem;
}

.avatar-img {
  width: 60px;
  height: 60px;
  border-radius: 50%;
  object-fit: cover;
  border: 2px solid #e9ecef;
}

.avatar-placeholder {
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

.user-info {
  text-align: center;
}

.user-name {
  margin: 0 0 0.5rem 0;
  color: #333;
  font-size: 1.2rem;
}

.user-email {
  margin: 0 0 1rem 0;
  color: #666;
  font-size: 0.9rem;
}

.user-dates {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.date-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.25rem 0;
  border-bottom: 1px solid #f1f3f4;
}

.date-item:last-child {
  border-bottom: none;
}

.date-label {
  font-size: 0.85rem;
  color: #666;
  font-weight: 500;
}

.date-value {
  font-size: 0.85rem;
  color: #333;
}

.user-actions {
  margin-top: 1.5rem;
  padding-top: 1rem;
  border-top: 1px solid #f1f3f4;
  display: flex;
  justify-content: center;
}

.btn {
  padding: 0.5rem 1rem;
  border: none;
  border-radius: 4px;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
  text-decoration: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}

.btn-edit {
  background-color: #007bff;
  color: white;
}

.btn-edit:hover {
  background-color: #0056b3;
  transform: translateY(-1px);
}

@media (max-width: 768px) {
  .users-page {
    padding: 1rem;
  }
  
  .page-header {
    flex-direction: column;
    align-items: flex-start;
    gap: 1rem;
  }
  
  .users-grid {
    grid-template-columns: 1fr;
  }
}
</style>