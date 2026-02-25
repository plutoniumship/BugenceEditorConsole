import type { ActionModule } from "@bugence/core";
import { deleteCanvasSection } from "../../store/canvasStore";

const deleteSectionAction: ActionModule = {
  canExecute: (context) => Boolean(context.pageId && context.sectionId),
  async execute(context) {
    if (!context.pageId || !context.sectionId) {
      return {
        status: "error",
        message: "Page and section context are required."
      };
    }

    try {
      await deleteCanvasSection(context.pageId, context.sectionId);
      return {
        status: "success",
        message: "Section deleted."
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

export default deleteSectionAction;

