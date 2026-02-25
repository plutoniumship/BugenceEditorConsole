import { defineConfig } from "vite";
import { fileURLToPath, URL } from "node:url";

const resolvePort = (value: string | undefined, fallback: number) => {
  if (!value) {
    return fallback;
  }

  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
};

const backendTarget =
  process.env.BUGENCE_BACKEND_URL?.trim() || "https://localhost:7044";

export default defineConfig({
  resolve: {
    alias: {
      "@bugence/analytics": fileURLToPath(
        new URL("../analytics/src/index.ts", import.meta.url)
      )
    }
  },
  server: {
    host: process.env.BUGENCE_DASHBOARD_HOST ?? "localhost",
    port: resolvePort(process.env.BUGENCE_DASHBOARD_PORT, 5173),
    strictPort: true,
    proxy: {
      "/api": {
        target: backendTarget,
        changeOrigin: true,
        secure: false
      }
    }
  },
  optimizeDeps: {
    entries: ["src/site.ts"],
    include: ["@bugence/analytics"]
  },
  build: {
    sourcemap: true,
    emptyOutDir: false,
    outDir: "../../BugenceEditConsole/wwwroot/js",
    lib: {
      entry: "./src/site.ts",
      name: "BugenceDashboard",
      formats: ["iife"],
      fileName: () => "site.js"
    }
  }
});
