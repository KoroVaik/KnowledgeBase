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
      '/api': {
        target: 'http://localhost:5244',
        // Vite rewrites Host to the target by default. The backend builds the OAuth
        // redirect_uri from that header, so Google would be sent back to :5244 - past the
        // proxy, to a port that serves no SPA in dev.
        changeOrigin: false,
      },
    },
  },
})
