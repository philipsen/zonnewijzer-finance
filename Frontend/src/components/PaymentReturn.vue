<template>
  <div class="payment-return">
    <div class="card">
      <div class="icon">✓</div>
      <h2>Payment processed</h2>
      <p>You can close this window.</p>
    </div>
  </div>
</template>

<script>
export default {
  name: 'PaymentReturn',
  mounted() {
    // Notify the opener (MyInvoices page) to refresh, then close this tab
    if (window.opener) {
      window.opener.postMessage({ type: 'payment-return' }, window.location.origin)
      window.close()
    }
  }
}
</script>

<style scoped>
.payment-return {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 60vh;
}

.card {
  text-align: center;
  padding: 3rem 2rem;
  background: #f0fdf4;
  border: 1px solid #bbf7d0;
  border-radius: 12px;
  max-width: 360px;
}

.icon {
  font-size: 3rem;
  color: #16a34a;
}

h2 {
  margin: 0.5rem 0 0.25rem;
  color: #15803d;
}

p {
  color: #4b7c5a;
  margin: 0;
}
</style>
