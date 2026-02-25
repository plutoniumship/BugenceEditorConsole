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
      "@bugence/core": fileURLToPath(new URL("../core/src/index.ts", import.meta.url))
    }
  },
  server: {
    host: process.env.BUGENCE_CANVAS_HOST ?? "localhost",
    port: resolvePort(process.env.BUGENCE_CANVAS_PORT, 5174),
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
    entries: ["src/bugence-editor.ts"]
  },
  build: {
    sourcemap: true,
    emptyOutDir: false,
    outDir: "../../BugenceEditConsole/wwwroot/editor",
    lib: {
      entry: "./src/bugence-editor.ts",
      name: "BugenceCanvasEditor",
      formats: ["iife"],
      fileName: () => "bugence-editor.js"
    }
  }
});
