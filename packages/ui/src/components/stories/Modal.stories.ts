import type { Meta, StoryObj } from "@storybook/html";
import { Modal } from "../Modal";

const meta: Meta = {
  title: "Components/Modal",
  render: (args) => {
    const container = document.createElement("div");
    container.innerHTML = "";

    const modal = Modal({
      ...args,
      children: args.children || "<p>This is a modal body with some sample content.</p>",
      footer: args.footer || "<button class=\"storybook-btn\">Primary</button>"
    });

    if (Array.isArray(modal)) {
      modal.forEach((node) => container.append(node as Node));
    } else if (modal) {
      container.append(modal as Node);
    }

    return container;
  },
  args: {
    open: true,
    title: "Mission Control",
    description: "Schema-driven modal across dashboard and canvas",
    width: "md"
  }
};

export default meta;

export const Default: StoryObj = {};

export const Wide: StoryObj = {
  args: {
    width: "lg"
  }
};

export const WithoutHeader: StoryObj = {
  args: {
    title: undefined,
    description: undefined,
    children: "<p>Compact modal without header content.</p>"
  }
};
