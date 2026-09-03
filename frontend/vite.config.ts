import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Pinned: the backend CORS policy and .vscode/launch.json both hard-code 5173.
    // strictPort makes a busy port fail loudly instead of silently moving to 5174,
    // which would break API calls in a way that looks like a CORS misconfiguration.
    port: 5173,
    strictPort: true,
  },
})
