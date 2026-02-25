import type {
  SectionFieldSchema,
  SectionSchema,
  SectionSchemaRegistry,
  SidebarCardRegistry,
  WorkflowRegistry
} from "../types";

export type FieldDescriptor =
  | {
      kind: "input";
      field: SectionFieldSchema;
    }
  | {
      kind: "richtext";
      field: SectionFieldSchema;
    }
  | {
      kind: "html";
      field: SectionFieldSchema;
    }
  | {
      kind: "image";
      field: SectionFieldSchema;
    }
  | {
      kind: "select";
      field: SectionFieldSchema;
      options: Array<{ value: string; label: string }>;
    }
  | {
      kind: "toggle";
      field: SectionFieldSchema;
    };

export interface SectionRenderDescriptor {
  schema: SectionSchema;
  fields: FieldDescriptor[];
}

export function buildSectionDescriptor(
  schema: SectionSchema
): SectionRenderDescriptor {
  const fields: FieldDescriptor[] = schema.fields.map((field) => {
    switch (field.type) {
      case "richtext":
        return { kind: "richtext", field };
      case "html":
        return { kind: "html", field };
      case "image":
      case "media":
        return { kind: "image", field };
      case "select":
        return {
          kind: "select",
          field,
          options: []
        };
      case "toggle":
        return { kind: "toggle", field };
      default:
        return { kind: "input", field };
    }
  });

  return {
    schema,
    fields
  };
}

export function resolveSectionDescriptor(
  registry: SectionSchemaRegistry,
  sectionKey: string
): SectionRenderDescriptor | null {
  const schema = registry[sectionKey];
  if (!schema) {
    return null;
  }

  return buildSectionDescriptor(schema);
}

export function getSidebarCard(
  registry: SidebarCardRegistry,
  cardId: string
) {
  return registry[cardId] ?? null;
}

export function getWorkflow(
  registry: WorkflowRegistry,
  workflowId: string
) {
  return registry[workflowId] ?? null;
}
