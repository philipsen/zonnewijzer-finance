<template>
  <div id="app">
    <nav class="navbar">
      <div class="navbar-brand">
        <img src="/assets/zonnewijzer_logo.png" alt="Zonnewijzer Logo" class="logo">
        <h1>Zonnewijzer Finance</h1>
      </div>
      <nav v-if="user" ref="mainNav" class="main-nav" :class="{ 'nav-hidden': navOverflows }">
        <router-link to="/" class="nav-link">Home</router-link>
        <template v-if="user.role === 'Admin' && !previewMode">
          <router-link to="/users" class="nav-link">Users</router-link>
          <router-link to="/charging-users" class="nav-link">Charging Users</router-link>
          <router-link to="/charging-sessions" class="nav-link">Sessions</router-link>
          <router-link to="/charging-stations" class="nav-link">Stations</router-link>
          <router-link to="/upload-sessions" class="nav-link">Upload</router-link>
          <router-link to="/invoices" class="nav-link">Invoices</router-link>
          <router-link to="/payments" class="nav-link">Payments</router-link>
          <router-link to="/energy-prices" class="nav-link">Prices</router-link>
          <router-link to="/event-log" class="nav-link">Events</router-link>
        </template>
        <template v-else-if="user.role !== 'Admin' || previewMode">
          <router-link v-if="user.chargingUserId" to="/my-sessions" class="nav-link">My Sessions</router-link>
          <router-link v-if="user.chargingUserId" to="/my-invoices" class="nav-link">My Invoices</router-link>
        </template>
      </nav>

      <!-- Hamburger dropdown shown when nav overflows -->
      <div v-if="user && navOverflows" class="nav-dropdown-wrapper" ref="dropdownWrapper">
        <button class="hamburger-btn" @click="dropdownOpen = !dropdownOpen" :aria-expanded="dropdownOpen">
          <span></span><span></span><span></span>
        </button>
        <div v-if="dropdownOpen" class="nav-dropdown">
          <router-link to="/" class="dropdown-link" @click="dropdownOpen = false">Home</router-link>
          <template v-if="user.role === 'Admin' && !previewMode">
            <router-link to="/users" class="dropdown-link" @click="dropdownOpen = false">Users</router-link>
            <router-link to="/charging-users" class="dropdown-link" @click="dropdownOpen = false">Charging Users</router-link>
            <router-link to="/charging-sessions" class="dropdown-link" @click="dropdownOpen = false">Sessions</router-link>
            <router-link to="/charging-stations" class="dropdown-link" @click="dropdownOpen = false">Stations</router-link>
            <router-link to="/upload-sessions" class="dropdown-link" @click="dropdownOpen = false">Upload</router-link>
            <router-link to="/invoices" class="dropdown-link" @click="dropdownOpen = false">Invoices</router-link>
            <router-link to="/payments" class="dropdown-link" @click="dropdownOpen = false">Payments</router-link>
            <router-link to="/energy-prices" class="dropdown-link" @click="dropdownOpen = false">Prices</router-link>
            <router-link to="/event-log" class="dropdown-link" @click="dropdownOpen = false">Events</router-link>
          </template>
          <template v-else-if="user.role !== 'Admin' || previewMode">
            <router-link v-if="user.chargingUserId" to="/my-sessions" class="dropdown-link" @click="dropdownOpen = false">My Sessions</router-link>
            <router-link v-if="user.chargingUserId" to="/my-invoices" class="dropdown-link" @click="dropdownOpen = false">My Invoices</router-link>
          </template>
        </div>
      </div>

      <div class="nav-actions">
        <template v-if="!user">
          <!-- sign-in button is rendered on the Home page -->
        </template>
        <template v-else>
          <button v-if="user.role === 'Admin'" @click="togglePreviewMode" :class="['preview-toggle-btn', { active: previewMode }]">
            {{ previewMode ? 'Admin view' : 'User view' }}
          </button>
          <div class="user-info">
            <span>{{ user.name }}</span>
            <span v-if="user.role" :class="['role-badge', `role-${user.role.toLowerCase()}`]">{{ user.role }}</span>
          </div>
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
import { ref, onMounted, onBeforeUnmount, watch, nextTick, provide } from 'vue'
import { useRouter } from 'vue-router'
import axios from 'axios'

export default {
  name: 'App',
  setup() {
    const user = ref(null)
    const previewMode = ref(false)
    const router = useRouter()
    const googleClientId = '367881736099-4g29mnk5ho3a1iuosd5uo4ut284gbpql.apps.googleusercontent.com'
    const mainNav = ref(null)
    const dropdownWrapper = ref(null)
    const navOverflows = ref(false)
    const dropdownOpen = ref(false)
    let resizeObserver = null

    const checkNavOverflow = () => {
      nextTick(() => {
        const navbar = document.querySelector('.navbar')
        const nav = mainNav.value
        if (!navbar || !nav) return
        // Temporarily make nav visible to measure its natural width
        const wasHidden = nav.classList.contains('nav-hidden')
        nav.classList.remove('nav-hidden')
        nav.style.visibility = 'hidden'
        const navNaturalWidth = nav.scrollWidth
        nav.style.visibility = ''
        if (wasHidden) nav.classList.add('nav-hidden')

        const brand = document.querySelector('.navbar-brand')
        const actions = document.querySelector('.nav-actions')
        const available = navbar.clientWidth - (brand?.offsetWidth ?? 0) - (actions?.offsetWidth ?? 0) - 48
        navOverflows.value = navNaturalWidth > available
      })
    }

    let googleGsiInitialized = false
    const lastGoogleRenderError = ref('')

    const renderSigninButton = (el) => {
      lastGoogleRenderError.value = ''
      const target = el
      const id = window.google?.accounts?.id
      if (!target || !id) return

      try {
        if (!googleGsiInitialized) {
          id.initialize({
            client_id: googleClientId,
            callback: handleCredentialResponse
          })
          googleGsiInitialized = true
        }

        target.innerHTML = ''
        id.renderButton(target, {
          type: 'standard',
          shape: 'rectangular',
          theme: 'outline',
          text: 'signin_with',
          size: 'large',
          logo_alignment: 'left',
          width: 280
        })
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        lastGoogleRenderError.value = msg
        console.error('Google Sign-In renderButton failed:', err)
      }
    }

    provide('renderSigninButton', renderSigninButton)
    provide('lastGoogleRenderError', lastGoogleRenderError)
    provide('appUser', user)
    provide('previewMode', previewMode)

    const handleCredentialResponse = async (response) => {
      console.log('Google credential response:', response)
      try {
        const result = await axios.post('/api/auth/google', {
          credential: response.credential
        })
        user.value = result.data.user
        localStorage.setItem('token', result.data.token)
        window.location.reload()
      } catch (error) {
        console.error('Authentication failed:', error)
      }
    }

    const signOut = async () => {
      try {
        const token = localStorage.getItem('token')
        if (token) {
          await axios.post('/api/auth/logout', {}, {
            headers: { Authorization: `Bearer ${token}` }
          })
        }
      } catch {
        // ignore errors, proceed with local sign-out
      }
      user.value = null
      localStorage.removeItem('token')
      window.google?.accounts?.id?.disableAutoSelect()
      window.location.href = '/'
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
      window.handleCredentialResponse = handleCredentialResponse
      checkAuthStatus()

      resizeObserver = new ResizeObserver(() => checkNavOverflow())
      resizeObserver.observe(document.body)

      document.addEventListener('click', (e) => {
        if (dropdownWrapper.value && !dropdownWrapper.value.contains(e.target)) {
          dropdownOpen.value = false
        }
      })
    })

    onBeforeUnmount(() => {
      resizeObserver?.disconnect()
    })

    watch(user, () => {
      nextTick(() => checkNavOverflow())
    })

    const togglePreviewMode = () => {
      previewMode.value = !previewMode.value
      router.push('/')
    }

    return {
      user,
      previewMode,
      togglePreviewMode,
      googleClientId,
      mainNav,
      dropdownWrapper,
      navOverflows,
      dropdownOpen,
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

.nav-hidden {
  display: none !important;
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
  gap: 1.5rem;
}

.nav-dropdown-wrapper {
  position: relative;
}

.hamburger-btn {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  width: 28px;
  height: 20px;
  background: none;
  border: none;
  cursor: pointer;
  padding: 0;
}

.hamburger-btn span {
  display: block;
  height: 2px;
  background: #495057;
  border-radius: 2px;
  transition: background 0.2s;
}

.hamburger-btn:hover span {
  background: #007bff;
}

.nav-dropdown {
  position: absolute;
  top: calc(100% + 0.5rem);
  left: 0;
  background: white;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  box-shadow: 0 4px 12px rgba(0,0,0,0.12);
  min-width: 160px;
  z-index: 100;
  overflow: hidden;
}

.dropdown-link {
  display: block;
  padding: 0.65rem 1rem;
  text-decoration: none;
  color: #495057;
  font-weight: 500;
  font-size: 0.95rem;
  transition: background 0.15s;
}

.dropdown-link:hover,
.dropdown-link.router-link-active {
  background: #e3f2fd;
  color: #007bff;
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
    gap: 0.75rem;
    padding: 0.75rem 1rem;
  }

  .navbar-brand h1 {
    font-size: 1rem;
  }

  .logo {
    height: 28px;
  }

  .main-nav {
    gap: 0.25rem;
    flex-wrap: wrap;
    width: 100%;
  }

  .nav-link {
    padding: 0.4rem 0.6rem;
    font-size: 0.85rem;
  }

  .nav-actions {
    align-self: stretch;
    width: 100%;
    justify-content: space-between;
  }

  .user-info {
    min-width: unset;
    font-size: 0.85rem;
    align-items: flex-start;
    flex: 1;
  }

  .sign-out-btn {
    white-space: nowrap;
    font-size: 0.85rem;
    padding: 0.4rem 0.75rem;
  }

  main {
    padding: 0.75rem;
  }
}

.user-info {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.25rem;
  padding: 0.75rem 1rem;
  background-color: #ffffff;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  width: fit-content;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
}

.role-badge {
  padding: 0.25rem 0.5rem;
  border-radius: 12px;
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.role-base {
  background-color: #e3f2fd;
  color: #1565c0;
}

.role-admin {
  background-color: #fff3e0;
  color: #e65100;
}

.preview-toggle-btn {
  padding: 0.5rem 1rem;
  border: 1px solid #6c757d;
  background-color: #6c757d;
  color: white;
  border-radius: 4px;
  cursor: pointer;
  font-size: inherit;
  font-weight: 500;
  transition: all 0.2s ease;
  white-space: nowrap;
}

.preview-toggle-btn:hover {
  background-color: #545b62;
  border-color: #545b62;
}

.preview-toggle-btn.active {
  background-color: #1565c0;
  border-color: #1565c0;
  color: white;
}
.preview-toggle-btn.active:hover {
  background-color: #0d47a1;
  border-color: #0d47a1;
}
</style>