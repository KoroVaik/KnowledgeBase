import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    // 5173 is hard-coded in the backend CORS policy and .vscode/launch.json. strictPort
    // fails loudly instead of drifting to 5174, which looks like a CORS misconfiguration.
    port: 5173,
    strictPort: true,
    // All interfaces, so a phone on the same LAN can reach the dev server by IP.
    host: true,
  },
})
