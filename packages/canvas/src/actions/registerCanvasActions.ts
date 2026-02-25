import {
  getActionMeta,
  registerAction,
  type ActionRegistration
} from "@bugence/core";

const canvasActionRegistrations: ActionRegistration[] = [
  {
    meta: {
      id: "canvas.section.duplicate",
      title: "Duplicate Section",
      description: "Create a copy of the current section for iterative editing.",
      icon: "fa-regular fa-clone",
      capabilities: ["canvas.actions.duplicate"],
      category: "canvas",
      tags: ["section", "workflow"]
    },
    load: () => import("./handlers/duplicateSection").then((module) => module.default),
    cache: true
  },
  {
    meta: {
      id: "canvas.section.delete",
      title: "Delete Section",
      description: "Remove the section from the page.",
      icon: "fa-regular fa-trash-can",
      capabilities: ["canvas.actions.delete"],
      category: "canvas",
      tags: ["section", "cleanup"],
      requiresSelection: true
    },
    load: () => import("./handlers/deleteSection").then((module) => module.default),
    cache: true
  },
  {
    meta: {
      id: "canvas.page.publish",
      title: "Publish Updates",
      description: "Publish the current draft changes to the live site.",
      icon: "fa-solid fa-rocket",
      capabilities: ["canvas.actions.publish"],
      category: "workflow",
      tags: ["publish", "workflow"]
    },
    load: () => import("./handlers/publishPage").then((module) => module.default),
    cache: true
  },
  {
    meta: {
      id: "canvas.section.aiRewrite",
      title: "AI Rewrite",
      description: "Generate a concise rewrite for the selected section.",
      icon: "fa-regular fa-wand-magic-sparkles",
      capabilities: ["canvas.actions.aiRewrite"],
      category: "assistant",
      tags: ["section", "ai"],
      requiresSelection: true
    },
    load: () => import("./handlers/aiRewriteSection").then((module) => module.default),
    cache: true
  },
  {
    meta: {
      id: "canvas.section.createVariant",
      title: "Create Variant",
      description: "Spin up an alternate version of the section for experimentation.",
      icon: "fa-solid fa-flask",
      capabilities: ["canvas.actions.createVariant"],
      category: "experiments",
      tags: ["section", "experiment"],
      requiresSelection: true
    },
    load: () => import("./handlers/createVariant").then((module) => module.default),
    cache: true
  }
];

export function registerCanvasActions(options: { force?: boolean } = {}): void {
  canvasActionRegistrations.forEach((registration) => {
    if (!options.force && getActionMeta(registration.meta.id)) {
      return;
    }
    registerAction(registration);
  });
}

registerCanvasActions();

