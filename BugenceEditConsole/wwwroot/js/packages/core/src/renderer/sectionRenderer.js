export function buildSectionDescriptor(schema) {
    const fields = schema.fields.map((field) => {
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
export function resolveSectionDescriptor(registry, sectionKey) {
    const schema = registry[sectionKey];
    if (!schema) {
        return null;
    }
    return buildSectionDescriptor(schema);
}
export function getSidebarCard(registry, cardId) {
    return registry[cardId] ?? null;
}
export function getWorkflow(registry, workflowId) {
    return registry[workflowId] ?? null;
}
