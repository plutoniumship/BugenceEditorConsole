import type { WorkflowRegistry } from "../types";

export const workflowSchemas: WorkflowRegistry = {
  default_content_flow: {
    id: "default_content_flow",
    title: "Content Workflow",
    defaultStepId: "draft",
    steps: [
      {
        id: "draft",
        label: "Draft",
        description: "Edit sections and capture notes.",
        icon: "fa-solid fa-pencil",
        status: "draft",
        actions: [
          {
            id: "submit-for-review",
            label: "Submit for review",
            icon: "fa-solid fa-share",
            intent: "primary",
            target: "command",
            command: "workflow.submitReview"
          }
        ]
      },
      {
        id: "review",
        label: "Review",
        description: "Verify tone, accessibility, and QA checks.",
        icon: "fa-solid fa-clipboard-check",
        status: "review",
        blockers: ["missingAccessibilityScan", "unresolvedComments"],
        actions: [
          {
            id: "request-changes",
            label: "Request changes",
            icon: "fa-solid fa-comment-dots",
            target: "command",
            command: "workflow.requestChanges"
          },
          {
            id: "approve",
            label: "Approve",
            icon: "fa-solid fa-check",
            intent: "primary",
            target: "command",
            command: "workflow.approve"
          }
        ]
      },
      {
        id: "publish",
        label: "Publish",
        description: "Push live once all checks pass.",
        icon: "fa-solid fa-rocket",
        status: "publish",
        blockers: ["draftSectionsExist"],
        actions: [
          {
            id: "publish-now",
            label: "Publish now",
            icon: "fa-solid fa-rocket-launch",
            intent: "primary",
            target: "command",
            command: "canvas.publish"
          },
          {
            id: "schedule-publish",
            label: "Schedule publish",
            icon: "fa-solid fa-calendar",
            target: "modal",
            command: "workflow.schedulePublish"
          }
        ]
      }
    ]
  }
};

export type WorkflowSchemaId = keyof typeof workflowSchemas;
