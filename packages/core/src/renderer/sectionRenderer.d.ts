import type { SectionFieldSchema, SectionSchema, SectionSchemaRegistry, SidebarCardRegistry, WorkflowRegistry } from "../types";
export type FieldDescriptor = {
    kind: "input";
    field: SectionFieldSchema;
} | {
    kind: "richtext";
    field: SectionFieldSchema;
} | {
    kind: "html";
    field: SectionFieldSchema;
} | {
    kind: "image";
    field: SectionFieldSchema;
} | {
    kind: "select";
    field: SectionFieldSchema;
    options: Array<{
        value: string;
        label: string;
    }>;
} | {
    kind: "toggle";
    field: SectionFieldSchema;
};
export interface SectionRenderDescriptor {
    schema: SectionSchema;
    fields: FieldDescriptor[];
}
export declare function buildSectionDescriptor(schema: SectionSchema): SectionRenderDescriptor;
export declare function resolveSectionDescriptor(registry: SectionSchemaRegistry, sectionKey: string): SectionRenderDescriptor | null;
export declare function getSidebarCard(registry: SidebarCardRegistry, cardId: string): import("..").SidebarCardSchema;
export declare function getWorkflow(registry: WorkflowRegistry, workflowId: string): import("..").WorkflowSchema;
//# sourceMappingURL=sectionRenderer.d.ts.map