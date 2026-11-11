import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // The app calls /api/... on its own address and Vite passes it on to the backend.
    // The browser only ever talks to one address, so no cross-site (CORS) rules are needed.
    proxy: { "/api": "http://localhost:5158" },
  },
  test: {
    environment: "jsdom", // a pretend browser, so components can be rendered in tests
    setupFiles: "./src/test-setup.ts",
  },
});
