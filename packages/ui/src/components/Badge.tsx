import type { JSX } from "preact";
import "../styles/tokens.css";
import "../styles/badge.css";
import "../styles/badge.css";

export interface BadgeProps {
  tone?: "neutral" | "positive" | "warning" | "info";
  soft?: boolean;
  children: JSX.Element | JSX.Element[] | string;
}

const TONE_CLASS: Record<NonNullable<BadgeProps["tone"]>, string> = {
  neutral: "bugence-badge--neutral",
  positive: "bugence-badge--positive",
  warning: "bugence-badge--warning",
  info: "bugence-badge--info"
};

export function Badge({ tone = "neutral", soft = false, children }: BadgeProps) {
  const toneClass = TONE_CLASS[tone] ?? TONE_CLASS.neutral;
  const softClass = soft ? "bugence-badge--soft" : "";

  return (
    <span className={`bugence-badge ${toneClass} ${softClass}`.trim()}>
      {children}
    </span>
  );
}

export default Badge;
