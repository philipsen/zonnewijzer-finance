<template>
  <div id="app">
    <nav class="navbar">
      <div class="navbar-brand">
        <img src="/assets/zonnewijzer_logo.png" alt="Zonnewijzer Logo" class="logo">
        <h1>Finance App</h1>
      </div>
      <nav v-if="user" class="main-nav">
        <router-link to="/" class="nav-link">Home</router-link>
        <router-link to="/users" class="nav-link">Users</router-link>
        <router-link to="/charging-sessions" class="nav-link">Sessions</router-link>
        <router-link to="/upload-sessions" class="nav-link">Upload</router-link>
      </nav>
      <div class="nav-actions">
        <template v-if="!user">
          <div id="g_id_onload"
               :data-client_id="googleClientId"
               data-context="signin"
               data-ux_mode="popup"
               data-callback="handleCredentialResponse"
               data-auto_prompt="false">
          </div>
          <div class="g_id_signin"
               data-type="standard"
               data-shape="rectangular"
               data-theme="outline"
               data-text="signin_with"
               data-size="large"
               data-logo_alignment="left">
          </div>
        </template>
        <template v-else>
          <span>Welcome, {{ user.name }}</span>
          <button @click="signOut" class="sign-out-btn">Sign Out</button>
        </template>
      </div>
    </nav>
    <main>
      <router-view />
    </main>
  </div>
</template>

<script>
import { ref, onMounted } from 'vue'
import axios from 'axios'

export default {
  name: 'App',
  setup() {
    const user = ref(null)
    const googleClientId = '367881736099-4g29mnk5ho3a1iuosd5uo4ut284gbpql.apps.googleusercontent.com' 

    const handleCredentialResponse = async (response) => {
      console.log('Google credential response:', response)
      try {
        const result = await axios.post('/api/auth/google', {
          credential: response.credential
        })
        user.value = result.data.user
        localStorage.setItem('token', result.data.token)
      } catch (error) {
        console.error('Authentication failed:', error)
      }
    }

    const signOut = () => {
      user.value = null
      localStorage.removeItem('token')
      google.accounts.id.disableAutoSelect()
    }

    const checkAuthStatus = async () => {
      console.log('Checking authentication status...')
      const token = localStorage.getItem('token')
      if (token) {
        try {
          const response = await axios.get('/api/auth/user', {
            headers: { Authorization: `Bearer ${token}` }
          })
          user.value = response.data
        } catch (error) {
          localStorage.removeItem('token')
        }
      }
    }

    onMounted(() => {
      checkAuthStatus()
      window.handleCredentialResponse = handleCredentialResponse
    })

    return {
      user,
      googleClientId,
      signOut
    }
  }
}
</script>

<style>
body {
  margin: 0;
  font-family: sans-serif;
}

.navbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem 2rem;
  background-color: #f8f9fa;
  border-bottom: 1px solid #dee2e6;
}

.navbar-brand {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.logo {
  height: 40px;
  width: auto;
  object-fit: contain;
}

.main-nav {
  display: flex;
  gap: 2rem;
}

.nav-link {
  text-decoration: none;
  color: #495057;
  font-weight: 500;
  padding: 0.5rem 1rem;
  border-radius: 4px;
  transition: background-color 0.2s ease;
}

.nav-link:hover {
  background-color: #e9ecef;
}

.nav-link.router-link-active {
  color: #007bff;
  background-color: #e3f2fd;
}

.nav-actions {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.sign-out-btn {
  padding: 0.5rem 1rem;
  border: 1px solid #dc3545;
  background-color: #dc3545;
  color: white;
  border-radius: 4px;
  cursor: pointer;
}

.sign-out-btn:hover {
  background-color: #c82333;
}

main {
  padding: 2rem;
}

@media (max-width: 768px) {
  .navbar {
    flex-direction: column;
    align-items: flex-start;
    gap: 1rem;
    padding: 1rem;
  }
  
  .main-nav {
    gap: 1rem;
  }
  
  .nav-actions {
    align-self: flex-end;
  }
  
  main {
    padding: 1rem;
  }
}
</style>