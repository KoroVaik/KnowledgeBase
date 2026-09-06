import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    // strictPort fails loudly instead of drifting to 5174, which would silently break
    // the .vscode/launch.json URL.
    port: 5173,
    strictPort: true,
    // All interfaces, so a phone on the same LAN can reach the dev server by IP.
    host: true,
    // Keeps the browser on a single origin in dev, matching production where ASP.NET
    // serves the SPA itself: no CORS, and cookies behave the same in both.
    proxy: {
      '/api': 'http://localhost:5244',
    },
  },
})
