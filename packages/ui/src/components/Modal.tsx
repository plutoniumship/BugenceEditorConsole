import { JSX } from "preact";
import { useEffect } from "preact/hooks";
import "../styles/tokens.css";
import "../styles/modal.css";

export interface ModalProps {
  open: boolean;
  onClose?: () => void;
  title?: string;
  description?: string;
  width?: "sm" | "md" | "lg" | "xl";
  children: JSX.Element | JSX.Element[] | string;
  footer?: JSX.Element | JSX.Element[] | string;
}

const WIDTH_CLASS: Record<NonNullable<ModalProps["width"]>, string> = {
  sm: "max-w-md",
  md: "max-w-2xl",
  lg: "max-w-4xl",
  xl: "max-w-6xl"
};

export function Modal({
  open,
  onClose,
  title,
  description,
  width = "md",
  children,
  footer
}: ModalProps) {
  useEffect(() => {
    if (!open) {
      return;
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose?.();
      }
    };

    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [open, onClose]);

  if (!open) {
    return null;
  }

  return (
    <div className="bugence-modal" role="dialog" aria-modal="true">
      <div className="bugence-modal__backdrop" onClick={onClose} />
      <article className={`bugence-modal__panel ${WIDTH_CLASS[width]}`}>
        {(title || description) && (
          <header className="bugence-modal__header">
            {title ? <h2 className="bugence-modal__title">{title}</h2> : null}
            {description ? <p className="bugence-modal__description">{description}</p> : null}
          </header>
        )}
        <div className="bugence-modal__body">{children}</div>
        {footer ? <footer className="bugence-modal__footer">{footer}</footer> : null}
        <button
          type="button"
          className="bugence-modal__close"
          aria-label="Close"
          onClick={onClose}
        >
          &times;
        </button>
      </article>
    </div>
  );
}

export default Modal;
