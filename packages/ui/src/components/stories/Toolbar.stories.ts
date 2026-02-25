import type { Meta, StoryObj } from "@storybook/html";
import { Toolbar } from "../Toolbar";

const meta: Meta = {
  title: "Components/Toolbar",
  render: (args) => {
    const toolbar = Toolbar(args);
    const wrapper = document.createElement("div");
    wrapper.append(toolbar as Node);
    return wrapper;
  },
  args: {
    ariaLabel: "Rich text controls",
    buttons: [
      { id: "bold", label: "Bold" },
      { id: "italic", label: "Italic" },
      { id: "underline", label: "Underline" },
      { id: "color", label: "Color" }
    ],
    groups: [
      { id: "type", label: "Type", buttonIds: ["bold", "italic", "underline"] },
      { id: "color-group", buttonIds: ["color"] }
    ]
  }
};

export default meta;

export const Default: StoryObj = {};

export const ActiveState: StoryObj = {
  args: {
    buttons: [
      { id: "bold", label: "Bold", active: true },
      { id: "italic", label: "Italic" },
      { id: "underline", label: "Underline" }
    ],
    groups: undefined
  }
};
