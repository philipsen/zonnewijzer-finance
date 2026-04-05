<template>
  <div class="home">
    <h2>Welcome to the Zonnewijzer Finance App</h2>
    <div v-if="!isAuthenticated" class="auth-prompt">
      <p>Please sign in to access your financial data.</p>
      <div ref="signinButtonEl" class="signin-btn-wrapper"></div>
      <div class="divider"><span>or</span></div>
      <a href="/api/auth/github" class="github-btn">
        <svg class="github-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0024 12c0-6.63-5.37-12-12-12z"/></svg>
        Sign in with GitHub
      </a>
      <div v-if="authError" class="auth-error">{{ authError }}</div>
    </div>

    <div v-else-if="needsAccess" class="pending-access">
      <div class="pending-icon">🔒</div>
      <h3>Account not yet activated</h3>
      <p>Your account (<strong>{{ appUser.email }}</strong>) has been created but is not yet linked to a charging account.</p>
      <p>Please contact <strong>Tom</strong> (the administrator) to request access.</p>
    </div>

    <div v-else class="dashboard">
      <p>Dashboard content will go here.</p>
      <div class="features">
        <div class="feature-card">
          <router-link to="/charging-sessions" class="card-link">
            <h3>Charging Sessions</h3>
            <p>View and manage charging sessions</p>
          </router-link>
        </div>
        <div class="feature-card">
          <router-link to="/invoices" class="card-link">
            <h3>Invoices</h3>
            <p>Generate and download invoices</p>
          </router-link>
        </div>
      </div>
    </div>
  </div>
</template>

<script>
import { computed, ref, onMounted, inject } from 'vue'
import axios from 'axios'

export default {
  name: 'Home',
  setup() {
    const isAuthenticated = computed(() => localStorage.getItem('token') !== null)
    const appUser = inject('appUser', ref(null))

    const needsAccess = computed(() =>
      isAuthenticated.value &&
      appUser.value &&
      appUser.value.role === 'Base' &&
      !appUser.value.chargingUserId
    )

    const signinButtonEl = ref(null)
    const renderSigninButton = inject('renderSigninButton', null)
    const authError = ref('')

    onMounted(async () => {
      // Handle GitHub OAuth callback token
      const params = new URLSearchParams(window.location.search)
      const githubToken = params.get('github_token')
      const errorParam = params.get('auth_error')

      if (githubToken) {
        try {
          // Verify token and get user info
          const response = await axios.get('/api/auth/user', {
            headers: { Authorization: `Bearer ${githubToken}` }
          })
          localStorage.setItem('token', githubToken)
          window.history.replaceState({}, '', '/')
          window.location.reload()
        } catch {
          authError.value = 'GitHub sign-in failed. Please try again.'
          window.history.replaceState({}, '', '/')
        }
        return
      }

      if (errorParam) {
        const messages = {
          github_denied: 'GitHub sign-in was cancelled.',
          github_no_email: 'No verified email found on your GitHub account.',
          github_error: 'GitHub sign-in failed. Please try again.'
        }
        authError.value = messages[errorParam] || 'Sign-in failed.'
        window.history.replaceState({}, '', '/')
      }

      if (!isAuthenticated.value && renderSigninButton && signinButtonEl.value) {
        renderSigninButton(signinButtonEl.value)
      }
    })

    return {
      isAuthenticated,
      needsAccess,
      appUser,
      signinButtonEl,
      authError
    }
  }
}
</script>

<style scoped>
.home {
  max-width: 1200px;
  margin: 0 auto;
}

.auth-prompt {
  text-align: center;
  padding: 3rem 2rem;
  background-color: #f8f9fa;
  border-radius: 8px;
  margin: 2rem 0;
}

.signin-btn-wrapper {
  display: flex;
  justify-content: center;
  margin-top: 1.5rem;
}

.divider {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin: 1.25rem auto;
  max-width: 240px;
  color: #aaa;
  font-size: 0.85rem;
}

.divider::before,
.divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: #ddd;
}

.github-btn {
  display: inline-flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.55rem 1.25rem;
  background: #24292f;
  color: #fff;
  border-radius: 6px;
  text-decoration: none;
  font-size: 0.95rem;
  font-weight: 500;
  transition: background 0.15s;
}

.github-btn:hover {
  background: #3d444b;
}

.github-icon {
  width: 20px;
  height: 20px;
  fill: currentColor;
  flex-shrink: 0;
}

.auth-error {
  margin-top: 1rem;
  color: #b00020;
  font-size: 0.9rem;
}

.pending-access {
  text-align: center;
  padding: 3rem 2rem;
  background: #fff8e1;
  border: 1px solid #ffe082;
  border-radius: 8px;
  margin: 2rem 0;
}

.pending-icon {
  font-size: 2.5rem;
  margin-bottom: 0.75rem;
}

.pending-access h3 {
  margin: 0 0 0.75rem;
  color: #5d4037;
}

.pending-access p {
  margin: 0.4rem 0;
  color: #5d4037;
}

.features {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 1rem;
  margin-top: 2rem;
}

.feature-card {
  padding: 1.5rem;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  background-color: #fff;
  transition: transform 0.2s ease, box-shadow 0.2s ease;
}

.feature-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
}

.feature-card h3 {
  margin-top: 0;
  color: #495057;
}

.card-link {
  text-decoration: none;
  color: inherit;
  display: block;
}

.card-link:hover {
  text-decoration: none;
  color: inherit;
}
</style>