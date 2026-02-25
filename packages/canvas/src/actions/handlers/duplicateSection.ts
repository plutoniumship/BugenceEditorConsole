import type { ActionModule } from "@bugence/core";
import { duplicateCanvasSection } from "../../store/canvasStore";

interface DuplicateParams {
  variantLabel?: string;
}

const duplicateSectionAction: ActionModule<DuplicateParams> = {
  canExecute: (context) => Boolean(context.pageId && context.sectionId),
  async execute(context, params) {
    if (!context.pageId || !context.sectionId) {
      return {
        status: "error",
        message: "Page and section context are required."
      };
    }

    try {
      const variantLabel =
        typeof params === "object" && params
          ? ("variantLabel" in params ? params.variantLabel : undefined)
          : undefined;

      const duplicated = await duplicateCanvasSection(context.pageId, context.sectionId, {
        variantLabel
      });

      return {
        status: "success",
        message: "Section duplicated.",
        data: {
          sectionId: duplicated.id,
          variantLabel: variantLabel ?? null
        }
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

export default duplicateSectionAction;

