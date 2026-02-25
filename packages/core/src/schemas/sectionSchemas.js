export const sectionSchemas = {
    hero_title: {
        id: "hero_title",
        title: "Hero Headline",
        description: "Primary headline rendered in hero region.",
        contentType: "Text",
        defaults: {
            contentValue: "High-Tech Heroes, orchestrated in real time."
        },
        fields: [
            {
                id: "contentValue",
                type: "text",
                label: "Headline",
                helperText: "Max 120 characters. Use concise, compelling language.",
                required: true,
                maxLength: 120,
                toolbar: ["bold", "italic"],
                sanitizer: "basic",
                previewHint: "Displayed prominently above the fold."
            }
        ],
        preview: {
            visualType: "text",
            summaryField: "contentValue"
        }
    },
    hero_story: {
        id: "hero_story",
        title: "Hero Story",
        description: "Rich narrative block supporting the hero headline.",
        contentType: "RichText",
        fields: [
            {
                id: "contentValue",
                type: "richtext",
                label: "Narrative",
                helperText: "Supports bold, italics, links, and unordered lists.",
                required: true,
                toolbar: [
                    "bold",
                    "italic",
                    "underline",
                    "strike",
                    "unorderedList",
                    "orderedList",
                    "blockquote",
                    "h2",
                    "paragraph",
                    "link",
                    "removeFormat",
                    "color",
                    "highlight"
                ],
                allowedTags: ["p", "strong", "em", "a", "ul", "ol", "li", "blockquote"],
                allowedAttributes: ["href", "title"],
                sanitizer: "basic",
                previewHint: "Used in hero overlay and preview cards."
            }
        ],
        preview: {
            visualType: "text",
            summaryField: "contentValue",
            secondaryField: "previousContentValue"
        }
    },
    hero_metrics: {
        id: "hero_metrics",
        title: "Impact Metrics",
        description: "HTML snippet rendering highlight metrics.",
        contentType: "Html",
        fields: [
            {
                id: "contentValue",
                type: "html",
                label: "Metric List",
                helperText: "Provide an unordered list (`<ul><li>..`) of metrics.",
                toolbar: ["bold", "italic", "unorderedList", "orderedList", "removeFormat"],
                sanitizer: "strict",
                allowedTags: ["ul", "li", "strong", "span"],
                previewHint: "Displayed as bullet list next to hero content."
            }
        ],
        preview: {
            visualType: "metric",
            summaryField: "contentValue"
        }
    },
    hero_image: {
        id: "hero_image",
        title: "Hero Image",
        description: "Primary imagery framing the hero message.",
        contentType: "Image",
        fields: [
            {
                id: "image",
                type: "image",
                label: "Hero Image",
                helperText: "Upload a high-resolution image (max 5MB).",
                required: true,
                accept: ["image/png", "image/jpeg", "image/webp"],
                maxFileSizeMB: 5
            },
            {
                id: "mediaAltText",
                type: "text",
                label: "Alt Text",
                helperText: "Describe the visual. Required for accessibility.",
                required: true,
                maxLength: 150
            }
        ],
        preview: {
            visualType: "image",
            summaryField: "mediaPath",
            secondaryField: "mediaAltText"
        }
    },
    booking_cta: {
        id: "booking_cta",
        title: "Booking CTA",
        description: "Primary call-to-action copy for booking funnel.",
        contentType: "Text",
        fields: [
            {
                id: "contentValue",
                type: "text",
                label: "Call-to-action",
                helperText: "Short imperative phrase (e.g., \"Schedule a discovery call\").",
                required: true,
                maxLength: 90,
                toolbar: ["bold", "italic"],
                previewHint: "Rendered on button and analytics tiles."
            }
        ],
        preview: {
            visualType: "cta",
            summaryField: "contentValue"
        }
    },
    booking_visual: {
        id: "booking_visual",
        title: "Booking Visual",
        description: "Supporting imagery for the booking storyline.",
        contentType: "Image",
        fields: [
            {
                id: "image",
                type: "image",
                label: "Visual Asset",
                helperText: "Landscape imagery recommended.",
                required: true,
                accept: ["image/png", "image/jpeg", "image/webp"],
                maxFileSizeMB: 5
            },
            {
                id: "mediaAltText",
                type: "text",
                label: "Alt Text",
                helperText: "Describe the imagery context.",
                required: true,
                maxLength: 150
            }
        ],
        preview: {
            visualType: "image",
            summaryField: "mediaPath"
        }
    }
};
//# sourceMappingURL=sectionSchemas.js.map