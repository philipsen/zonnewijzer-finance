<template>
  <div class="home">
    <h2>Welcome to the Zonnewijzer Finance App</h2>
    <div v-if="!isAuthenticated" class="auth-prompt">
      <p>Please sign in to access your financial data.</p>
      <div ref="signinButtonEl" class="signin-btn-wrapper"></div>
      <p v-if="lastGoogleRenderError" class="gsi-render-error" role="alert">
        Google button error: {{ lastGoogleRenderError }}
      </p>
      <div class="divider"><span>or</span></div>
      <a href="/api/auth/github" class="github-btn">
        <svg class="github-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0024 12c0-6.63-5.37-12-12-12z"/></svg>
        Sign in with GitHub
      </a>
      <div class="divider"><span>or</span></div>
      <div class="email-auth">
        <div class="auth-mode-toggle">
          <button type="button" :class="{ active: authMode === 'login' }" @click="authMode = 'login'">Email login</button>
          <button type="button" :class="{ active: authMode === 'register' }" @click="authMode = 'register'">Create account</button>
        </div>
        <form class="email-auth-form" @submit.prevent="submitEmailAuth">
          <input
            v-if="authMode === 'register'"
            v-model.trim="authName"
            type="text"
            placeholder="Name (optional)"
            autocomplete="name"
          >
          <input
            v-model.trim="authEmail"
            type="email"
            placeholder="Email"
            autocomplete="email"
            required
          >
          <input
            v-model="authPassword"
            type="password"
            placeholder="Password"
            autocomplete="current-password"
            minlength="8"
            required
          >
          <button type="submit" class="email-auth-submit" :disabled="emailAuthLoading">
            {{ emailAuthLoading ? 'Please wait…' : (authMode === 'login' ? 'Sign in' : 'Create account') }}
          </button>
        </form>
        <button
          v-if="authMode === 'login'"
          type="button"
          class="forgot-password-btn"
          :disabled="forgotPasswordLoading"
          @click="submitForgotPassword"
        >
          {{ forgotPasswordLoading ? 'Sending…' : 'Forgot password?' }}
        </button>
        <p v-if="forgotPasswordMessage" class="forgot-password-message">{{ forgotPasswordMessage }}</p>
      </div>
      <div v-if="authError" class="auth-error">{{ authError }}</div>
    </div>

    <div v-else-if="needsAccess" class="pending-access">
      <div class="pending-icon">🔒</div>
      <h3>Account not yet activated</h3>
      <p>Your account (<strong>{{ appUser.email }}</strong>) has been created but is not yet linked to a charging account.</p>
      <p>Please contact <strong>Wim</strong> (the administrator) to request access.</p>
    </div>

    <div v-else class="dashboard">
      <div v-if="isAppAdminView && !previewMode" class="admin-tools">
        <p class="admin-tools-label">Administration</p>
        <button type="button" class="btn-wipe" @click="openWipeModal">
          Wipe all data…
        </button>
        <button
          v-if="isOwnerView"
          type="button"
          class="btn-gnucash-wipe"
          @click="openGnucashWipeModal"
        >
          Clear GnuCash app data…
        </button>
      </div>
      <p>Dashboard content will go here.</p>
      <div class="features">
        <div class="feature-card">
          <router-link :to="isAppAdminView && !previewMode ? '/charging-sessions' : '/my-sessions'" class="card-link">
            <h3>Charging Sessions</h3>
            <p>View and manage charging sessions</p>
          </router-link>
        </div>
        <div class="feature-card">
          <router-link :to="isAppAdminView && !previewMode ? '/invoices' : '/my-invoices'" class="card-link">
            <h3>Invoices</h3>
            <p>Generate and download invoices</p>
          </router-link>
        </div>
      </div>
    </div>

    <Teleport to="body">
      <div v-if="wipeModalOpen" class="modal-backdrop" @click.self="closeWipeModal">
        <div
          class="modal-panel"
          role="dialog"
          aria-modal="true"
          aria-labelledby="wipe-modal-title"
          tabindex="-1"
          @keydown.esc="closeWipeModal"
        >
          <h2 id="wipe-modal-title" class="modal-title">Wipe operational data?</h2>
          <p class="modal-warning">
            This permanently deletes charging sessions, invoices, Mollie payments, energy prices, charging user profiles,
            and event logs. <strong>User accounts are kept</strong> (login remains possible); links from users to charging profiles are cleared.
            This cannot be undone.
          </p>
          <p v-if="wipeError" class="modal-error">{{ wipeError }}</p>
          <div class="modal-actions">
            <button type="button" class="btn-cancel" :disabled="wipeLoading" @click="closeWipeModal">
              Cancel
            </button>
            <button
              type="button"
              class="btn-confirm-wipe"
              :disabled="wipeLoading"
              @click="executeWipe"
            >
              {{ wipeLoading ? 'Wiping…' : 'Wipe database' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>

    <Teleport to="body">
      <div v-if="gnucashWipeModalOpen" class="modal-backdrop" @click.self="closeGnucashWipeModal">
        <div
          class="modal-panel modal-panel--gnucash"
          role="dialog"
          aria-modal="true"
          aria-labelledby="gnucash-wipe-modal-title"
          tabindex="-1"
          @keydown.esc="closeGnucashWipeModal"
        >
          <h2 id="gnucash-wipe-modal-title" class="modal-title">Clear GnuCash app data?</h2>
          <p class="modal-warning">
            This removes <strong>all invoices whose id starts with <code>INV-</code></strong> and
            <strong>all customers whose id starts with <code>CU-</code></strong> from the GnuCash book
            (posted transactions and lots are removed with those invoices). Other GnuCash data is not touched.
            Requires a configured GnuCash sync service. This cannot be undone.
          </p>
          <label class="gnucash-wipe-dry">
            <input v-model="gnucashWipeDryRun" type="checkbox">
            Dry run only (count what would be removed, do not save)
          </label>
          <p v-if="gnucashWipeError" class="modal-error">{{ gnucashWipeError }}</p>
          <div class="modal-actions">
            <button type="button" class="btn-cancel" :disabled="gnucashWipeLoading" @click="closeGnucashWipeModal">
              Cancel
            </button>
            <button
              type="button"
              class="btn-confirm-gnucash-wipe"
              :disabled="gnucashWipeLoading"
              @click="executeGnucashWipe"
            >
              {{ gnucashWipeLoading ? 'Working…' : (gnucashWipeDryRun ? 'Dry run' : 'Clear GnuCash') }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script>
import { computed, ref, onMounted, onBeforeUnmount, inject, nextTick, watch } from 'vue'
import axios from 'axios'
import { isAppAdmin, isOwner } from '../utils/roles'

export default {
  name: 'Home',
  setup() {
    const isAuthenticated = computed(() => localStorage.getItem('token') !== null)
    const appUser = inject('appUser', ref(null))
    const previewMode = inject('previewMode', ref(false))

    const isAppAdminView = computed(() => isAppAdmin(appUser.value?.role))
    const isOwnerView = computed(() => isOwner(appUser.value?.role))

    const needsAccess = computed(() =>
      isAuthenticated.value &&
      appUser.value &&
      appUser.value.role === 'Base' &&
      !appUser.value.chargeCardId
    )

    const signinButtonEl = ref(null)
    const renderSigninButton = inject('renderSigninButton', null)
    const lastGoogleRenderError = inject('lastGoogleRenderError', ref(''))
    const authError = ref('')
    const authMode = ref('login')
    const authEmail = ref('')
    const authPassword = ref('')
    const authName = ref('')
    const emailAuthLoading = ref(false)
    const forgotPasswordLoading = ref(false)
    const forgotPasswordMessage = ref('')

    const wipeModalOpen = ref(false)
    const wipeLoading = ref(false)
    const wipeError = ref('')

    const gnucashWipeModalOpen = ref(false)
    const gnucashWipeLoading = ref(false)
    const gnucashWipeError = ref('')
    const gnucashWipeDryRun = ref(false)

    const openWipeModal = () => {
      wipeError.value = ''
      wipeModalOpen.value = true
    }

    const closeWipeModal = () => {
      if (wipeLoading.value) return
      wipeModalOpen.value = false
      wipeError.value = ''
    }

    const openGnucashWipeModal = () => {
      gnucashWipeError.value = ''
      gnucashWipeDryRun.value = false
      gnucashWipeModalOpen.value = true
    }

    const closeGnucashWipeModal = () => {
      if (gnucashWipeLoading.value) return
      gnucashWipeModalOpen.value = false
      gnucashWipeError.value = ''
    }

    const executeGnucashWipe = async () => {
      gnucashWipeError.value = ''
      gnucashWipeLoading.value = true
      try {
        const token = localStorage.getItem('token')
        const { data } = await axios.post(
          '/api/invoices/gnucash/wipe-app',
          {},
          {
            params: { dryRun: gnucashWipeDryRun.value },
            headers: { Authorization: `Bearer ${token}` }
          }
        )
        gnucashWipeLoading.value = false
        closeGnucashWipeModal()
        const dr = data.dryRun ? ' (dry run — nothing saved)' : ''
        window.alert(
          `GnuCash: removed ${data.invoicesRemoved} INV-* invoice(s) and ${data.customersRemoved} CU-* customer(s).${dr}`
        )
      } catch (err) {
        console.error('GnuCash wipe failed:', err)
        if (err.response?.status === 403) {
          gnucashWipeError.value = 'Only the owner account can clear GnuCash app data.'
        } else if (err.response?.data && typeof err.response.data === 'string') {
          gnucashWipeError.value = err.response.data
        } else if (err.response?.data?.message) {
          gnucashWipeError.value = err.response.data.message
        } else {
          gnucashWipeError.value = err.response?.data?.detail || err.message || 'Request failed.'
        }
      } finally {
        gnucashWipeLoading.value = false
      }
    }

    const executeWipe = async () => {
      wipeError.value = ''
      wipeLoading.value = true
      try {
        const token = localStorage.getItem('token')
        await axios.post(
          '/api/auth/admin/wipe-database',
          { confirm: true },
          { headers: { Authorization: `Bearer ${token}` } }
        )
        wipeLoading.value = false
        closeWipeModal()
        window.location.reload()
      } catch (err) {
        console.error('Wipe failed:', err)
        if (err.response?.status === 403) {
          wipeError.value = 'Only administrators can wipe the database.'
        } else if (err.response?.data?.message) {
          wipeError.value = err.response.data.message
        } else {
          wipeError.value = 'Wipe failed. Please try again or check the server logs.'
        }
      } finally {
        wipeLoading.value = false
      }
    }

    const submitEmailAuth = async () => {
      authError.value = ''
      forgotPasswordMessage.value = ''
      emailAuthLoading.value = true
      try {
        const endpoint = authMode.value === 'register' ? '/api/auth/register' : '/api/auth/login'
        const payload = authMode.value === 'register'
          ? { email: authEmail.value, password: authPassword.value, name: authName.value }
          : { email: authEmail.value, password: authPassword.value }

        const response = await axios.post(endpoint, payload)
        localStorage.setItem('token', response.data.token)
        window.location.reload()
      } catch (err) {
        if (err.response?.status === 401) {
          authError.value = 'Invalid email or password.'
        } else if (err.response?.status === 409) {
          authError.value = 'An account with this email already exists.'
        } else if (typeof err.response?.data === 'string') {
          authError.value = err.response.data
        } else {
          authError.value = 'Authentication failed. Please try again.'
        }
      } finally {
        emailAuthLoading.value = false
      }
    }

    const submitForgotPassword = async () => {
      authError.value = ''
      forgotPasswordMessage.value = ''
      forgotPasswordLoading.value = true
      try {
        await axios.post('/api/auth/forgot-password', {
          email: authEmail.value,
          resetBaseUrl: window.location.origin
        })
        forgotPasswordMessage.value = 'If your account exists, a reset link has been sent.'
      } catch {
        forgotPasswordMessage.value = 'If your account exists, a reset link has been sent.'
      } finally {
        forgotPasswordLoading.value = false
      }
    }

    const googleButtonPresent = () => {
      const el = signinButtonEl.value
      return !!el && (el.childElementCount > 0 || (el.textContent || '').trim().length > 2)
    }

    const tryRenderGoogle = () => {
      nextTick(() => {
        if (isAuthenticated.value || !renderSigninButton || !signinButtonEl.value) return
        if (!window.google?.accounts?.id) return
        renderSigninButton(signinButtonEl.value)
      })
    }

    let googlePollTimer = null

    const startGoogleReadyPoll = () => {
      if (googlePollTimer) return
      let attempts = 0
      const maxAttempts = 80
      googlePollTimer = setInterval(() => {
        attempts++
        if (isAuthenticated.value || attempts > maxAttempts) {
          clearInterval(googlePollTimer)
          googlePollTimer = null
          return
        }
        if (googleButtonPresent()) {
          clearInterval(googlePollTimer)
          googlePollTimer = null
          return
        }
        tryRenderGoogle()
      }, 200)
    }

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
          return
        } catch {
          authError.value = 'GitHub sign-in failed. Please try again.'
          window.history.replaceState({}, '', '/')
        }
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

      tryRenderGoogle()
      window.addEventListener('google-gsi-ready', tryRenderGoogle)

      if (window.__googleGsiReady || window.google?.accounts?.id) {
        queueMicrotask(() => tryRenderGoogle())
      }

      requestAnimationFrame(() => tryRenderGoogle())
      startGoogleReadyPoll()
    })

    onBeforeUnmount(() => {
      window.removeEventListener('google-gsi-ready', tryRenderGoogle)
      if (googlePollTimer) {
        clearInterval(googlePollTimer)
        googlePollTimer = null
      }
    })

    watch(
      () => [isAuthenticated.value, signinButtonEl.value],
      () => tryRenderGoogle(),
      { flush: 'post', immediate: true }
    )

    return {
      isAuthenticated,
      needsAccess,
      appUser,
      isAppAdminView,
      isOwnerView,
      previewMode,
      signinButtonEl,
      authError,
      authMode,
      authEmail,
      authPassword,
      authName,
      emailAuthLoading,
      forgotPasswordLoading,
      forgotPasswordMessage,
      lastGoogleRenderError,
      wipeModalOpen,
      wipeLoading,
      wipeError,
      openWipeModal,
      closeWipeModal,
      executeWipe,
      gnucashWipeModalOpen,
      gnucashWipeLoading,
      gnucashWipeError,
      gnucashWipeDryRun,
      openGnucashWipeModal,
      closeGnucashWipeModal,
      executeGnucashWipe,
      submitEmailAuth,
      submitForgotPassword
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
  align-items: center;
  margin-top: 1.5rem;
  min-height: 48px;
  min-width: 200px;
  width: 100%;
  max-width: 320px;
  margin-left: auto;
  margin-right: auto;
}

.gsi-render-error {
  margin-top: 0.75rem;
  color: #b00020;
  font-size: 0.9rem;
  max-width: 36rem;
  margin-left: auto;
  margin-right: auto;
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

.email-auth {
  margin: 1rem auto 0;
  width: 100%;
  max-width: 320px;
}

.auth-mode-toggle {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.4rem;
  margin-bottom: 0.6rem;
}

.auth-mode-toggle button {
  padding: 0.45rem 0.5rem;
  border: 1px solid #ced4da;
  border-radius: 6px;
  background: white;
  cursor: pointer;
}

.auth-mode-toggle button.active {
  background: #4a6fa5;
  border-color: #4a6fa5;
  color: white;
}

.email-auth-form {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.email-auth-form input {
  border: 1px solid #ced4da;
  border-radius: 6px;
  padding: 0.55rem 0.7rem;
  font-size: 0.95rem;
}

.email-auth-submit {
  border: none;
  border-radius: 6px;
  padding: 0.6rem 0.8rem;
  background: #4a6fa5;
  color: white;
  font-weight: 600;
  cursor: pointer;
}

.email-auth-submit:hover:not(:disabled) {
  background: #3f5f8c;
}

.email-auth-submit:disabled {
  opacity: 0.7;
  cursor: default;
}

.forgot-password-btn {
  margin-top: 0.55rem;
  background: transparent;
  border: none;
  color: #4a6fa5;
  cursor: pointer;
  text-decoration: underline;
  font-size: 0.88rem;
}

.forgot-password-btn:disabled {
  opacity: 0.6;
  cursor: default;
}

.forgot-password-message {
  margin: 0.4rem 0 0;
  color: #2f5132;
  font-size: 0.85rem;
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

.admin-tools {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
  padding: 1rem 1.25rem;
  margin-bottom: 1.5rem;
  background: #fff5f5;
  border: 1px solid #f5c2c7;
  border-radius: 8px;
}

.admin-tools-label {
  margin: 0;
  font-size: 0.9rem;
  font-weight: 600;
  color: #842029;
}

.btn-wipe {
  background: #fff;
  color: #842029;
  border: 1px solid #f5c2c7;
  padding: 0.45rem 0.85rem;
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-wipe:hover {
  background: #f8d7da;
}

.btn-gnucash-wipe {
  background: #fff;
  color: #0d6efd;
  border: 1px solid #9ec5fe;
  padding: 0.45rem 0.85rem;
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-gnucash-wipe:hover {
  background: #e7f1ff;
}

.modal-panel--gnucash .modal-title {
  color: #084298;
}

.gnucash-wipe-dry {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.9rem;
  color: #333;
  margin: 0 0 0.5rem 0;
  cursor: pointer;
}

.btn-confirm-gnucash-wipe {
  background: #0d6efd;
  color: #fff;
  border: none;
  padding: 0.5rem 1rem;
  border-radius: 6px;
  font-weight: 600;
  cursor: pointer;
}

.btn-confirm-gnucash-wipe:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-confirm-gnucash-wipe:not(:disabled):hover {
  background: #0b5ed7;
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

.btn-confirm-wipe {
  background: #842029;
  color: #fff;
  border: none;
  padding: 0.5rem 1rem;
  border-radius: 6px;
  font-weight: 600;
  cursor: pointer;
}

.btn-confirm-wipe:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-confirm-wipe:not(:disabled):hover {
  background: #6a1a22;
}
</style>