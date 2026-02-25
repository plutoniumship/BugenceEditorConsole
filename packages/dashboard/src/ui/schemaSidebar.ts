import {
  sidebarCards,
  workflowSchemas,
  getSidebarCard,
  getWorkflow,
  resolveMetrics
} from "@bugence/core";

type MetricKey = "draftCount" | "lastPublishedAtUtc" | "accessibilityScore" | "brokenLinkCount";

interface SidebarMetricValue {
  raw: unknown;
  formatted?: string;
}

type MetricValueMap = Record<string, SidebarMetricValue>;

const METRIC_IDS: MetricKey[] = [
  "draftCount",
  "lastPublishedAtUtc",
  "accessibilityScore",
  "brokenLinkCount"
];

const CARD_SEQUENCE = ["publishing_overview", "quality_checks", "quick_actions"] as const;

export function initSchemaSidebar() {
  const root = document.querySelector<HTMLElement>("[data-schema-sidebar]");
  if (!root) {
    return;
  }

  const cardsHost = root.querySelector<HTMLElement>("[data-schema-cards]") ?? root;
  const workflowHost = root.querySelector<HTMLElement>("[data-schema-workflow]");

  const sourceData = collectMetricSource(root);

  void (async () => {
    const metrics = await resolveSidebarMetricValues(root.dataset.pageId, sourceData);

    CARD_SEQUENCE.forEach((cardId) => {
      const schema = getSidebarCard(sidebarCards, cardId);
      if (!schema) {
        return;
      }
      const card = renderCard(schema, metrics);
      cardsHost.appendChild(card);
    });
  })();

  if (workflowHost) {
    const workflow = getWorkflow(workflowSchemas, "default_content_flow");
    if (workflow) {
      workflowHost.appendChild(renderWorkflow(workflow));
    }
  }
}

function renderCard(schema: ReturnType<typeof getSidebarCard>, metrics: MetricValueMap) {
  const article = document.createElement("article");
  article.className = "tilt on-white dashboard-card animate-fadeUp";

  const cardInner = document.createElement("div");
  cardInner.className = "dashboard-card__inner space-y-5 p-6";

  const title = document.createElement("div");
  title.innerHTML = `
    <h3 class="font-display text-xl">${schema?.title ?? ""}</h3>
    ${schema?.description ? `<p class="text-sm text-ink/70">${schema.description}</p>` : ""}
  `;
  cardInner.appendChild(title);

  if (schema?.metrics?.length) {
    const metricGrid = document.createElement("div");
    metricGrid.className = "dashboard-card__metric-grid";
    schema.metrics.forEach((metric) => {
      const metricValue = metric.valueField ? metrics[metric.valueField] : undefined;
      const displayValue = metricValue
        ? metricValue.formatted ?? formatMetric(metricValue.raw, metric.formatter)
        : formatMetric(undefined, metric.formatter);
      metricGrid.appendChild(
        renderMetric(
          metric.label,
          displayValue,
          metric.description,
          metric.icon,
          metric.emphasis
        )
      );
    });
    cardInner.appendChild(metricGrid);
  }

  if (schema?.actions?.length) {
    const actionBar = document.createElement("div");
    actionBar.className = "flex flex-wrap gap-3";
    schema.actions.forEach((action) => {
      const button = document.createElement("button");
      button.type = "button";
      button.className = [
        "inline-flex items-center gap-2 rounded-full px-4 py-2 text-sm font-semibold transition",
        action.intent === "primary"
          ? "bg-black text-white hover:-translate-y-px"
          : action.intent === "danger"
            ? "bg-bugence-coral text-white"
            : "border border-black/10 hover:bg-black/5"
      ].join(" ");
      button.dataset.command = action.command ?? "";
      if (action.icon) {
        const icon = document.createElement("span");
        icon.className = `${action.icon}`;
        button.appendChild(icon);
      }
      button.appendChild(document.createTextNode(action.label));
      actionBar.appendChild(button);
    });
    cardInner.appendChild(actionBar);
  }

  if (schema?.footerHint) {
    const hint = document.createElement("p");
    hint.className = "text-xs text-ink/50";
    hint.textContent = schema.footerHint;
    cardInner.appendChild(hint);
  }

  article.appendChild(cardInner);
  return article;
}

function safeNumber(value: string | undefined, fallback = 0): number {
  if (value === undefined) {
    return fallback;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function collectMetricSource(root: HTMLElement): Record<string, unknown> {
  return {
    draftCount: safeNumber(root.dataset.draftCount, 0),
    lastPublishedAtUtc: root.dataset.lastPublished ?? root.dataset.lastPublishedAtUtc ?? null,
    accessibilityScore: safeNumber(root.dataset.accessibilityScore, 0),
    brokenLinkCount: safeNumber(root.dataset.brokenLinkCount, 0),
    lastSyncedAtUtc: root.dataset.lastSyncedAtUtc ?? null
  };
}

async function resolveSidebarMetricValues(
  pageId: string | undefined,
  source: Record<string, unknown>
): Promise<MetricValueMap> {
  try {
    const resolved = await resolveMetrics(METRIC_IDS, {
      pageId,
      source: "dashboard",
      data: source
    });

    const fallback = buildFallbackMetricMap(source);
    Object.entries(resolved).forEach(([key, result]) => {
      if (!result) {
        return;
      }
      fallback[key] = {
        raw: result.value ?? source[key],
        formatted: result.formatted
      };
    });
    return fallback;
  } catch (error) {
    console.error("[schemaSidebar] Failed to resolve metrics", error);
    return buildFallbackMetricMap(source);
  }
}

function buildFallbackMetricMap(source: Record<string, unknown>): MetricValueMap {
  return METRIC_IDS.reduce<MetricValueMap>((acc, key) => {
    acc[key] = { raw: source[key] };
    return acc;
  }, {} as MetricValueMap);
}

function renderMetric(label: string, value: string, description?: string, icon?: string, emphasis: string = "default") {
  const wrapper = document.createElement("div");
  wrapper.className = "dashboard-card__metric";
  if (icon) {
    const iconEl = document.createElement("span");
    iconEl.className = `${icon} text-accent`;
    wrapper.appendChild(iconEl);
  }
  const labelEl = document.createElement("p");
  labelEl.className = "dashboard-card__metric-label";
  labelEl.textContent = label;
  wrapper.appendChild(labelEl);

  const valueEl = document.createElement("p");
  valueEl.className = "dashboard-card__metric-value";
  if (emphasis === "warning") {
    valueEl.classList.add("text-bugence-coral");
  }
  valueEl.textContent = value;
  wrapper.appendChild(valueEl);

  if (description) {
    const desc = document.createElement("p");
    desc.className = "dashboard-card__metric-desc";
    desc.textContent = description;
    wrapper.appendChild(desc);
  }

  return wrapper;
}

function formatMetric(value: unknown, formatter?: string) {
  if (value === undefined || value === null) {
    return "--";
  }

  if (typeof value === "string") {
    const trimmed = value.trim();
    if (!trimmed) {
      return "--";
    }
    if (!formatter) {
      return trimmed;
    }
    value = trimmed;
  }

  if (formatter === "percentage") {
    if (typeof value === "string" && value.includes("%")) {
      return value;
    }
    const numeric = typeof value === "number" ? value : Number(value);
    if (Number.isFinite(numeric)) {
      const clamped = Math.min(Math.max(numeric, 0), 1);
      return `${Math.round(clamped * 100)}%`;
    }
    return String(value);
  }

  if (formatter === "datetime") {
    try {
      const date = value instanceof Date ? value : new Date(String(value));
      return date.toLocaleString();
    } catch {
      return String(value);
    }
  }

  if (formatter === "number") {
    const numeric = typeof value === "number" ? value : Number(value);
    if (Number.isFinite(numeric)) {
      return numeric.toLocaleString();
    }
  }

  return String(value);
}

function renderWorkflow(workflow: NonNullable<ReturnType<typeof getWorkflow>>) {
  const wrapper = document.createElement("article");
  wrapper.className = "tilt on-white dashboard-card animate-fadeUp";

  const inner = document.createElement("div");
  inner.className = "dashboard-card__inner space-y-4 p-6";

  const title = document.createElement("div");
  title.innerHTML = `
    <h3 class="font-display text-xl">${workflow.title}</h3>
    <p class="text-sm text-ink/70">Track progress from draft to publish.</p>
  `;
  inner.appendChild(title);

  const list = document.createElement("ol");
  list.className = "space-y-3";

  workflow.steps.forEach((step) => {
    const item = document.createElement("li");
    item.className = "flex items-start gap-3";
    item.innerHTML = `
      <span class="flex h-9 w-9 items-center justify-center rounded-full bg-black/5 text-black">
        <span class="${step.icon ?? "fa-solid fa-circle-dot"}"></span>
      </span>
      <div class="space-y-1">
        <p class="font-semibold">${step.label}</p>
        ${step.description ? `<p class="text-xs text-ink/60">${step.description}</p>` : ""}
        ${
          step.blockers?.length
            ? `<p class="text-xs text-bugence-coral">Blockers: ${step.blockers.join(", ")}</p>`
            : ""
        }
      </div>
    `;
    list.appendChild(item);
  });

  inner.appendChild(list);
  wrapper.appendChild(inner);
  return wrapper;
}
