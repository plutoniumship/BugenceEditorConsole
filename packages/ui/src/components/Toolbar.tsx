import type { JSX } from "preact";
import "../styles/tokens.css";
import "../styles/toolbar.css";

export interface ToolbarButtonProps {
  id: string;
  label: string;
  icon?: JSX.Element;
  disabled?: boolean;
  active?: boolean;
  onClick?: () => void;
}

export interface ToolbarProps {
  ariaLabel?: string;
  buttons: ToolbarButtonProps[];
  groups?: Array<{ id: string; label?: string; buttonIds: string[] }>;
}

export function Toolbar({ ariaLabel = "Formatting controls", buttons, groups }: ToolbarProps) {
  const buttonLookup = new Map(buttons.map((button) => [button.id, button]));

  const renderButton = (button: ToolbarButtonProps) => (
    <button
      type="button"
      key={button.id}
      className={`bugence-toolbar__button ${button.active ? "bugence-toolbar__button--active" : ""}`}
      onClick={button.onClick}
      disabled={button.disabled}
    >
      {button.icon ? <span className="bugence-toolbar__icon">{button.icon}</span> : null}
      <span className="bugence-toolbar__label">{button.label}</span>
    </button>
  );

  if (!groups?.length) {
    return (
      <div className="bugence-toolbar" role="toolbar" aria-label={ariaLabel}>
        {buttons.map((button) => renderButton(button))}
      </div>
    );
  }

  return (
    <div className="bugence-toolbar" role="toolbar" aria-label={ariaLabel}>
      {groups.map((group, index) => {
        const groupButtons = group.buttonIds
          .map((id) => buttonLookup.get(id))
          .filter((button): button is ToolbarButtonProps => Boolean(button));

        if (groupButtons.length === 0) {
          return null;
        }

        return (
          <div key={group.id} className="bugence-toolbar__group">
            {group.label ? <span className="bugence-toolbar__group-label">{group.label}</span> : null}
            <div className="bugence-toolbar__group-body">
              {groupButtons.map((button) => renderButton(button))}
            </div>
            {index < groups.length - 1 ? <span className="bugence-toolbar__divider" /> : null}
          </div>
        );
      })}
    </div>
  );
}

export default Toolbar;
