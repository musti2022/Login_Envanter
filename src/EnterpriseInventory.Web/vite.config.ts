/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

// React, /api and /hubs are served from the same origin in production (IIS).
// In development the Vite dev server proxies /api and /hubs to the ASP.NET Core API.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const apiTarget = env.VITE_DEV_API_PROXY_TARGET || 'https://localhost:7261'

  return {
    plugins: [react()],
    build: {
      rolldownOptions: {
        output: {
          codeSplitting: {
            groups: [
              { name: 'mui', test: /node_modules[\\/](@mui|@emotion)[\\/]/ },
              { name: 'vendor', test: /node_modules[\\/]/ },
            ],
          },
        },
      },
    },
    server: {
      proxy: {
        '/api': { target: apiTarget, changeOrigin: false, secure: true },
        '/hubs': { target: apiTarget, changeOrigin: false, secure: true, ws: true },
      },
    },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: './src/test/setup.ts',
      // The browser tests in e2e/ run with Playwright (npm run test:e2e).
      include: ['src/**/*.test.{ts,tsx}'],
      // Form tests click through many MUI selects; with every file running in parallel one can take several seconds.
      testTimeout: 15_000,
    },
  }
})
