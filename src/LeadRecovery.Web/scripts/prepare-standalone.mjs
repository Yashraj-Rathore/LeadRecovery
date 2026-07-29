import { cp, mkdir } from "node:fs/promises";

const standaloneApplication =
  ".next/standalone/src/LeadRecovery.Web/.next";

await mkdir(standaloneApplication, { recursive: true });
await cp(".next/static", `${standaloneApplication}/static`, {
  recursive: true,
});
