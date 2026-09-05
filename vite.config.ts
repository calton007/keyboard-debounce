import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  clearScreen: false,
  build: { outDir: "dist/web" },
  test: { environment: "jsdom", include: ["frontend/**/*.test.{ts,tsx}"] },
});
