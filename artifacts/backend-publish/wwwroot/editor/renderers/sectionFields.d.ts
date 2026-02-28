import type { SectionFieldSchema, SectionRenderDescriptor } from "@bugence/core";
interface RenderSectionFieldsArgs {
    descriptor: SectionRenderDescriptor | null;
    section?: any;
    element?: Element | null;
}
export interface TextFieldRenderResult {
    kind: "text";
    surface: HTMLElement;
    helperElement?: HTMLElement;
    helperText?: string;
    toolbarTokens?: string[];
    fieldSchema?: SectionFieldSchema;
    sanitize(value: string): {
        storageValue: string;
        html: string;
        plain: string;
        wasTruncated: boolean;
    };
}
export interface ImageFieldRenderResult {
    kind: "image";
    container: HTMLElement;
    fileInput: HTMLInputElement;
    altInput: HTMLInputElement;
    preview: HTMLImageElement;
    imageField?: SectionFieldSchema;
    altField?: SectionFieldSchema;
    sanitizeAlt(value: string): string;
    accept?: string[];
    maxFileSizeMB?: number;
}
export type SectionFieldRenderResult = TextFieldRenderResult | ImageFieldRenderResult;
export declare function renderSectionFields({ descriptor, section, element }: RenderSectionFieldsArgs): SectionFieldRenderResult;
export {};
