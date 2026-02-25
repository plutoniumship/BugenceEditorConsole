import type { StorylineEvent } from "./types";

const DATE_TIME_FORMAT = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric"
});

function formatHighlight(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return DATE_TIME_FORMAT.format(date);
}

export function renderStoryline(host: HTMLElement, events: StorylineEvent[]): void {
  host.innerHTML = "";

  if (!events.length) {
    const empty = document.createElement("p");
    empty.className = "insights-analytics__empty";
    empty.textContent = "No storyline events detected for this range.";
    host.appendChild(empty);
    return;
  }

  const list = document.createElement("ul");
  list.className = "insights-storyline__list";

  events.forEach((event) => {
    const item = document.createElement("li");
    item.className = "insights-storyline__item";

    const icon = document.createElement("span");
    icon.className = `insights-storyline__icon ${event.icon}`;
    icon.setAttribute("aria-hidden", "true");

    const content = document.createElement("div");
    content.className = "insights-storyline__content";

    const heading = document.createElement("div");
    heading.className = "insights-storyline__heading";
    const title = document.createElement("strong");
    title.textContent = event.title;
    const timestamp = document.createElement("span");
    timestamp.className = "insights-storyline__timestamp";
    timestamp.textContent = formatHighlight(event.highlightUtc);
    heading.append(title, timestamp);

    const summary = document.createElement("p");
    summary.textContent = event.summary;

    content.append(heading);
    if (event.accent) {
      const accent = document.createElement("span");
      accent.className = "insights-storyline__accent";
      accent.textContent = event.accent;
      content.append(accent);
    }
    content.append(summary);

    if (typeof event.changes === "number" && !Number.isNaN(event.changes)) {
      const badge = document.createElement("span");
      badge.className = "insights-storyline__badge";
      badge.textContent = `${event.changes.toLocaleString()} change${Math.abs(event.changes) === 1 ? "" : "s"}`;
      content.append(badge);
    }

    item.append(icon, content);
    list.appendChild(item);
  });

  host.appendChild(list);
}
