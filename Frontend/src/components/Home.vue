<template>
  <div class="home">
    <h2>Welcome to the Finance App</h2>
    <div v-if="!isAuthenticated" class="auth-prompt">
      <p>Please sign in with Google to access your financial data.</p>
    </div>
    <div v-else class="dashboard">
      <p>Dashboard content will go here.</p>
      <div class="features">
        <div class="feature-card">
          <h3>Expenses</h3>
          <p>Track your spending</p>
        </div>
        <div class="feature-card">
          <h3>Income</h3>
          <p>Monitor your earnings</p>
        </div>
        <div class="feature-card">
          <h3>Reports</h3>
          <p>View financial reports</p>
        </div>
      </div>
    </div>
  </div>
</template>

<script>
import { computed } from 'vue'

export default {
  name: 'Home',
  setup() {
    const isAuthenticated = computed(() => {
      return localStorage.getItem('token') !== null
    })

    return {
      isAuthenticated
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
  padding: 2rem;
  background-color: #f8f9fa;
  border-radius: 8px;
  margin: 2rem 0;
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
}

.feature-card h3 {
  margin-top: 0;
  color: #495057;
}
</style>