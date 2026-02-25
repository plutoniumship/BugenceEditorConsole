import type { StorybookConfig } from "@storybook/html-vite";

const config: StorybookConfig = {
  stories: [
    "../src/**/*.stories.@(ts|tsx|mdx)"
  ],
  addons: [
    "@storybook/addon-essentials",
    "@storybook/addon-links",
    "@storybook/addon-interactions"
  ],
  framework: {
    name: "@storybook/html-vite",
    options: {}
  },
  docs: {
    autodocs: true
  }
};

export default config;
