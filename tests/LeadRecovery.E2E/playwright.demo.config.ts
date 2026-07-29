import { defineConfig } from "@playwright/test";

import base from "./playwright.config";

export default defineConfig(base, {
  testIgnore: [],
  testMatch: "**/demo-video.spec.ts",
  timeout: 120_000,
  outputDir: "./demo-results",
  reporter: "list",
  use: {
    ...base.use,
    viewport: { width: 1280, height: 720 },
    video: { mode: "on", size: { width: 1280, height: 720 } },
    trace: "off",
    screenshot: "off",
  },
});
