import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 3000,
    proxy: {
      '/api': {
        target: 'http://localhost:5293',  // Updated to match development backend port
        changeOrigin: true,
        secure: false
      }
    }
  }
})