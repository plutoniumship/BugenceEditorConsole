import type { Meta, StoryObj } from "@storybook/html";
import { Badge } from "../Badge";

const meta: Meta = {
  title: "Components/Badge",
  render: (args) => {
    const badge = Badge(args);
    const container = document.createElement("div");
    container.append(badge as Node);
    return container;
  },
  args: {
    children: "In Review",
    tone: "info",
    soft: false
  }
};

export default meta;

export const Info: StoryObj = {};

export const Positive: StoryObj = {
  args: {
    tone: "positive",
    children: "Published"
  }
};

export const WarningSoft: StoryObj = {
  args: {
    tone: "warning",
    soft: true,
    children: "Needs Attention"
  }
};
