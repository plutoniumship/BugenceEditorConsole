import type { ActionModule } from "@bugence/core";
import { publishCanvasPage } from "../../store/canvasStore";

const publishPageAction: ActionModule = {
  canExecute: (context) => Boolean(context.pageId),
  async execute(context) {
    if (!context.pageId) {
      return {
        status: "error",
        message: "Page context is required."
      };
    }

    try {
      await publishCanvasPage(context.pageId);
      return {
        status: "success",
        message: "Publish request sent."
      };
    } catch (error) {
      return {
        status: "error",
        message: error instanceof Error ? error.message : String(error),
        data: { error }
      };
    }
  }
};

export default publishPageAction;

