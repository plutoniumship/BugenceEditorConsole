import type { ActionModule } from "@bugence/core";
import { getCanvasState, saveCanvasSection } from "../../store/canvasStore";

function stripHtml(value: string): string {
  return value.replace(/<[^>]+>/g, " ").replace(/\s+/g, " ").trim();
}

function capitaliseSentence(value: string): string {
  if (!value.length) {
    return value;
  }
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function synthesiseRewrite(value: string): string {
  const containsHtml = /<\w/i.test(value);
  const text = containsHtml ? stripHtml(value) : value;
  const sentences = text
    .split(/(?<=[.!?])\s+/)
    .map((sentence) => sentence.trim())
    .filter(Boolean);

  if (!sentences.length) {
    return value;
  }

  const unique = Array.from(new Set(sentences));
  const limited = unique.slice(0, 3).map((sentence) => {
    const hasTerminalPunctuation = /[.!?]$/.test(sentence);
    const prepared = hasTerminalPunctuation ? sentence : `${sentence}.`;
    return capitaliseSentence(prepared);
  });

  const rewritten = limited.join(" ");
  if (containsHtml) {
    return `<p>${rewritten}</p>`;
  }

  return rewritten;
}

const aiRewriteSectionAction: ActionModule = {
  canExecute: (context) => Boolean(context.pageId && context.sectionId),
  async execute(context) {
    if (!context.pageId || !context.sectionId) {
      return {
        status: "error",
        message: "Page and section context are required."
      };
    }

    const state = getCanvasState();
    const target = state.sections.find((section) => section.id === context.sectionId);
    if (!target) {
      return {
        status: "error",
        message: "Section not found."
      };
    }

    if (target.contentType === "Image") {
      return {
        status: "skipped",
        message: "AI rewrite is only available for text sections."
      };
    }

    const originalContent = target.contentValue ?? "";
    const rewritten = synthesiseRewrite(originalContent);
    if (!rewritten || rewritten.trim() === originalContent.trim()) {
      return {
        status: "skipped",
        message: "No rewrite suggestion generated."
      };
    }

    try {
      await saveCanvasSection(context.pageId, {
        sectionId: target.id,
        contentValue: rewritten
      });

      return {
        status: "success",
        message: "Section content rewritten.",
        data: {
          before: originalContent,
          after: rewritten
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

export default aiRewriteSectionAction;
