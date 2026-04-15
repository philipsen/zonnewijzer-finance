<template>
  <div class="reset-password-page">
    <div class="card">
      <h2>Reset password</h2>
      <p class="help">Enter your new password for your account.</p>

      <form @submit.prevent="submitReset" class="reset-form">
        <input
          v-model="newPassword"
          type="password"
          minlength="8"
          placeholder="New password"
          autocomplete="new-password"
          required
        >
        <input
          v-model="confirmPassword"
          type="password"
          minlength="8"
          placeholder="Confirm new password"
          autocomplete="new-password"
          required
        >
        <button type="submit" :disabled="loading">
          {{ loading ? 'Resetting…' : 'Reset password' }}
        </button>
      </form>

      <p v-if="message" class="message success">{{ message }}</p>
      <p v-if="error" class="message error">{{ error }}</p>
    </div>
  </div>
</template>

<script>
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import axios from 'axios'

export default {
  name: 'ResetPassword',
  setup() {
    const route = useRoute()
    const router = useRouter()
    const newPassword = ref('')
    const confirmPassword = ref('')
    const loading = ref(false)
    const message = ref('')
    const error = ref('')

    const submitReset = async () => {
      message.value = ''
      error.value = ''
      if (newPassword.value.length < 8) {
        error.value = 'Password must be at least 8 characters.'
        return
      }
      if (newPassword.value !== confirmPassword.value) {
        error.value = 'Passwords do not match.'
        return
      }

      const token = route.query.token
      if (!token || typeof token !== 'string') {
        error.value = 'Reset token is missing.'
        return
      }

      loading.value = true
      try {
        await axios.post('/api/auth/reset-password', {
          token,
          newPassword: newPassword.value
        })
        message.value = 'Password reset successful. You can now sign in.'
        setTimeout(() => router.push('/'), 1000)
      } catch (err) {
        error.value = err.response?.data || 'Reset link is invalid or expired.'
      } finally {
        loading.value = false
      }
    }

    return { newPassword, confirmPassword, loading, message, error, submitReset }
  }
}
</script>

<style scoped>
.reset-password-page {
  display: flex;
  justify-content: center;
  align-items: flex-start;
  padding: 2rem 1rem;
}

.card {
  width: 100%;
  max-width: 420px;
  border: 1px solid #dee2e6;
  border-radius: 10px;
  padding: 1.25rem;
  background: white;
}

.help {
  color: #666;
  margin-top: 0;
}

.reset-form {
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
}

.reset-form input {
  border: 1px solid #ced4da;
  border-radius: 6px;
  padding: 0.55rem 0.7rem;
}

.reset-form button {
  border: none;
  border-radius: 6px;
  padding: 0.6rem 0.8rem;
  background: #4a6fa5;
  color: white;
  font-weight: 600;
  cursor: pointer;
}

.reset-form button:disabled {
  opacity: 0.7;
  cursor: default;
}

.message {
  margin: 0.8rem 0 0;
  font-size: 0.9rem;
}

.success {
  color: #2f5132;
}

.error {
  color: #842029;
}
</style>
