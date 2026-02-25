import type { JSX } from "preact";
import "../styles/tokens.css";
import "../styles/timeline.css";

export interface TimelineItem {
  id: string;
  title: string;
  description?: string;
  timestamp?: string;
  icon?: JSX.Element;
  tone?: "neutral" | "positive" | "warning" | "info";
}

export interface TimelineProps {
  items: TimelineItem[];
  emptyMessage?: string;
}

export function Timeline({ items, emptyMessage = "No activity yet." }: TimelineProps) {
  if (items.length === 0) {
    return <p className="bugence-timeline__empty">{emptyMessage}</p>;
  }

  return (
    <ol className="bugence-timeline">
      {items.map((item, index) => (
        <li key={item.id} className={`bugence-timeline__item bugence-timeline__item--${item.tone ?? "neutral"}`}>
          <div className="bugence-timeline__icon">
            {item.icon ?? <span className="bugence-timeline__dot" />}
          </div>
          <div className="bugence-timeline__content">
            <div className="bugence-timeline__header">
              <h3>{item.title}</h3>
              {item.timestamp ? <time>{item.timestamp}</time> : null}
            </div>
            {item.description ? <p>{item.description}</p> : null}
          </div>
          {index < items.length - 1 ? <span className="bugence-timeline__line" /> : null}
        </li>
      ))}
    </ol>
  );
}

export default Timeline;
