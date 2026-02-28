import { spawn } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { chromium } from "playwright";

const root = process.cwd();
const baseUrl = "http://127.0.0.1:5117";
const artifactsDir = path.join(root, "artifacts", "analytics-regression");
await fs.mkdir(artifactsDir, { recursive: true });

const server = spawn(
  "dotnet",
  ["run", "--project", "BugenceEditConsole/BugenceEditConsole.csproj", "--urls", baseUrl],
  { cwd: root, stdio: ["ignore", "pipe", "pipe"] },
);

let serverLog = "";
server.stdout.on("data", (chunk) => {
  const text = chunk.toString();
  serverLog += text;
});
server.stderr.on("data", (chunk) => {
  const text = chunk.toString();
  serverLog += text;
});

async function waitForServer(timeoutMs = 120000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    try {
      const response = await fetch(`${baseUrl}/Auth/Login`, { redirect: "manual" });
      if (response.status >= 200) {
        return;
      }
    } catch {
      // retry
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error("Server did not start in time.");
}

async function ensureLoggedIn(page) {
  await page.goto(`${baseUrl}/Auth/Login`, { waitUntil: "domcontentloaded" });
  const emailInput = page.locator("input[name='Input.Email']");
  if (!(await emailInput.isVisible())) {
    return;
  }
  await emailInput.fill("admin@bugence.com");
  await page.locator("input[name='Input.Password']").fill("Bugence!2025");
  await page.locator("button[type='submit']").first().click();
  await page.waitForLoadState("networkidle");
}

async function captureTabSweep(page, prefix) {
  const tabs = page.locator(".tab-link");
  const count = await tabs.count();
  for (let i = 0; i < count; i++) {
    const tab = tabs.nth(i);
    const label = ((await tab.innerText()).trim() || `tab-${i}`).replace(/[^\w-]+/g, "_");
    await tab.click();
    await page.waitForTimeout(700);
    await page.screenshot({
      path: path.join(artifactsDir, `${prefix}-${String(i + 1).padStart(2, "0")}-${label}.png`),
      fullPage: true,
    });
  }
}

async function runSweep() {
  await waitForServer();
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  const report = {
    executedAtUtc: new Date().toISOString(),
    baseUrl,
    viewports: [],
    issues: [],
  };

  try {
    await ensureLoggedIn(page);
    const analyticsUrl = `${baseUrl}/Analytics/Index`;
    const viewports = [
      { name: "desktop-1440", width: 1440, height: 900 },
      { name: "tablet-834", width: 834, height: 1112 },
      { name: "mobile-390", width: 390, height: 844 },
      { name: "mobile-320", width: 320, height: 640 },
    ];

    for (const viewport of viewports) {
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await page.goto(analyticsUrl, { waitUntil: "networkidle" });
      if (page.url().includes("/Auth/Login")) {
        await ensureLoggedIn(page);
        await page.goto(analyticsUrl, { waitUntil: "networkidle" });
      }
      await page.waitForTimeout(1000);

      const metrics = await page.evaluate(() => {
        const root = document.documentElement;
        const overflowX = root.scrollWidth - root.clientWidth;
        const tabLinks = Array.from(document.querySelectorAll(".tab-link"));
        const tableControls = document.querySelectorAll(".table-toolbar, .module-tabs").length;
        const offscreen = [];
        for (const el of tabLinks) {
          const rect = el.getBoundingClientRect();
          if (rect.left < -1 || rect.right > window.innerWidth + 1) {
            offscreen.push(el.textContent?.trim() ?? "");
          }
        }
        return {
          overflowX,
          tabCount: tabLinks.length,
          offscreenTabs: offscreen,
          tableControls,
        };
      });

      report.viewports.push({ viewport, metrics });
      if (metrics.overflowX > 2) {
        report.issues.push(`${viewport.name}: horizontal overflow ${metrics.overflowX}px`);
      }
      if (metrics.offscreenTabs.length > 0) {
        report.issues.push(`${viewport.name}: offscreen tab chips: ${metrics.offscreenTabs.join(", ")}`);
      }
      await page.screenshot({
        path: path.join(artifactsDir, `${viewport.name}-overview.png`),
        fullPage: true,
      });
      await captureTabSweep(page, viewport.name);
    }

    await fs.writeFile(path.join(artifactsDir, "report.json"), JSON.stringify(report, null, 2), "utf8");
  } finally {
    await browser.close();
  }

  if (report.issues.length > 0) {
    throw new Error(`Regression issues found:\n- ${report.issues.join("\n- ")}`);
  }
}

let exitCode = 0;
try {
  await runSweep();
} catch (error) {
  exitCode = 1;
  await fs.writeFile(path.join(artifactsDir, "failure.log"), `${error?.stack || error}`, "utf8");
} finally {
  server.kill("SIGTERM");
  await fs.writeFile(path.join(artifactsDir, "server.log"), serverLog, "utf8");
}

process.exit(exitCode);

