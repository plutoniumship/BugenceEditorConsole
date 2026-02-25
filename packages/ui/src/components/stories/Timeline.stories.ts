import type { Meta, StoryObj } from "@storybook/html";
import { Timeline } from "../Timeline";

const meta: Meta = {
  title: "Components/Timeline",
  render: (args) => {
    const timeline = Timeline(args);
    const container = document.createElement("div");
    container.append(timeline as Node);
    return container;
  },
  args: {
    items: [
      {
        id: "publish",
        title: "Published index.html",
        description: "Deployed with 8 updated sections",
        timestamp: "2025-10-20 14:22",
        tone: "positive"
      },
      {
        id: "review",
        title: "Review requested",
        description: "A/B experiment copy ready",
        timestamp: "2025-10-20 13:40",
        tone: "info"
      }
    ]
  }
};

export default meta;

export const Default: StoryObj = {};

export const EmptyState: StoryObj = {
  args: {
    items: [],
    emptyMessage: "No timeline events yet."
  }
};
