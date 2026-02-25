import type { JSX } from "preact";
import { useEffect, useRef } from "preact/hooks";
import "../styles/tokens.css";
import "../styles/flyout.css";

export interface FlyoutProps {
  open: boolean;
  anchor?: HTMLElement | null;
  onClose?: () => void;
  placement?: "top" | "bottom" | "left" | "right";
  width?: "sm" | "md" | "lg";
  children: JSX.Element | JSX.Element[] | string;
}

const WIDTH_CLASS: Record<NonNullable<FlyoutProps["width"]>, string> = {
  sm: "bugence-flyout--sm",
  md: "bugence-flyout--md",
  lg: "bugence-flyout--lg"
};

export function Flyout({
  open,
  anchor,
  onClose,
  placement = "bottom",
  width = "md",
  children
}: FlyoutProps) {
  const flyoutRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) {
      return;
    }

    const handleClickAway = (event: MouseEvent) => {
      if (!flyoutRef.current) {
        return;
      }

      if (!flyoutRef.current.contains(event.target as Node)) {
        onClose?.();
      }
    };

    document.addEventListener("mousedown", handleClickAway);
    return () => document.removeEventListener("mousedown", handleClickAway);
  }, [open, onClose]);

  if (!open) {
    return null;
  }

  type FlyoutStyle = JSX.CSSProperties & Record<string, string>;
  const style: FlyoutStyle = {};
  if (anchor) {
    const rect = anchor.getBoundingClientRect();
    const anchorX = rect.left + window.scrollX + rect.width / 2;
    const anchorY = placement === "bottom"
      ? rect.bottom + window.scrollY + 12
      : rect.top + window.scrollY - 12;

    style["--flyout-anchor-x"] = `${anchorX}px`;
    style["--flyout-anchor-y"] = `${anchorY}px`;
  }

  return (
    <div
      className={`bugence-flyout ${WIDTH_CLASS[width]} bugence-flyout--${placement}`}
      ref={flyoutRef}
      style={style}
    >
      {children}
    </div>
  );
}

export default Flyout;
