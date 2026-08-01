import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    host: '0.0.0.0',
    port: 5173,
    cors: true
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
    rollupOptions: {
      output: {
        entryFileNames: '[name].js',
        chunkFileNames: '[name]-[hash].js',
        assetFileNames: '[name]-[hash][extname]'
      },
      input: {
        index: 'index.html',
        login: 'login.html',
        candidateDashboard: 'candidate-dashboard.html',
        recruiterDashboard: 'recruiter-dashboard.html',
        projectHiring: 'project-hiring.html'
      }
    }
  }
});