import type { EditorConfig } from "@bugence/core";

declare global {
  interface Window {
    __bugenceEditor?: EditorConfig;
  }
}

export {};
