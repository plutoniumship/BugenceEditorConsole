import type { ActionModule } from "@bugence/core";
import { duplicateCanvasSection } from "../../store/canvasStore";

interface VariantParams {
  variantLabel?: string;
}

const createVariantAction: ActionModule<VariantParams> = {
  canExecute: (context) => Boolean(context.pageId && context.sectionId),
  async execute(context, params) {
    if (!context.pageId || !context.sectionId) {
      return {
        status: "error",
        message: "Page and section context are required."
      };
    }

    const label =
      typeof params === "object" && params
        ? params.variantLabel ?? "B"
        : "B";

    try {
      const variant = await duplicateCanvasSection(context.pageId, context.sectionId, {
        variantLabel: label
      });

      return {
        status: "success",
        message: `Variant ${label} created.`,
        data: {
          sectionId: variant.id,
          variantLabel: label
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

export default createVariantAction;

