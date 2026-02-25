import type {
  SectionFieldSchema,
  SectionRenderDescriptor
} from "@bugence/core";

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

export type SectionFieldRenderResult =
  | TextFieldRenderResult
  | ImageFieldRenderResult;

const FALLBACK_TEXT_FIELD: SectionFieldSchema = {
  id: "contentValue",
  type: "richtext",
  label: "Content",
  helperText: "Edit the selected section.",
  sanitizer: "basic"
};

const FALLBACK_IMAGE_FIELD: SectionFieldSchema = {
  id: "image",
  type: "image",
  label: "Upload image"
};

const FALLBACK_ALT_FIELD: SectionFieldSchema = {
  id: "mediaAltText",
  type: "text",
  label: "Alt text",
  placeholder: "Describe this image",
  helperText: "Required for accessibility",
  required: true,
  maxLength: 150
};

const BASIC_ALLOWED_TAGS = ["p", "strong", "em", "a", "ul", "ol", "li", "blockquote", "br", "span"];
const BASIC_ALLOWED_ATTRS = ["href", "title", "target", "rel", "class", "style"];
const INLINE_STYLE_WHITELIST = new Set([
  "color",
  "background-color",
  "font-size",
  "font-family",
  "font-style",
  "font-weight",
  "font-variant",
  "text-decoration",
  "text-decoration-line",
  "text-transform",
  "letter-spacing",
  "line-height",
  "text-align"
]);

const sanitizeStyleAttribute = (value: string): string => {
  if (!value) {
    return "";
  }

  const sanitized = value
    .split(";")
    .map((segment) => segment.trim())
    .filter(Boolean)
    .map((segment) => {
      const [rawProperty, ...rawValueParts] = segment.split(":");
      if (!rawProperty || rawValueParts.length === 0) {
        return null;
      }

      const property = rawProperty.trim().toLowerCase();
      if (!INLINE_STYLE_WHITELIST.has(property)) {
        return null;
      }

      const rawValue = rawValueParts.join(":").trim();
      if (
        !rawValue.length ||
        /url\s*\(/i.test(rawValue) ||
        /expression/i.test(rawValue) ||
        /javascript:/i.test(rawValue)
      ) {
        return null;
      }

      return `${property}: ${rawValue}`;
    })
    .filter(Boolean)
    .join("; ");

  return sanitized;
};

const toToolbarTokens = (field?: SectionFieldSchema): string[] | undefined => {
  if (!field?.toolbar || field.toolbar.length === 0) {
    return undefined;
  }

  const tokens = new Set<string>();
  field.toolbar.forEach((token) => {
    const trimmed = token.trim();
    if (trimmed) {
      tokens.add(trimmed);
      if (trimmed === "link") {
        tokens.add("unlink");
        tokens.add("removeFormat");
      }
    }
  });

  tokens.add("undo");
  tokens.add("redo");
  tokens.add("fontFamily");
  tokens.add("fontSize");

  return Array.from(tokens);
};

export function renderSectionFields({
  descriptor,
  section,
  element
}: RenderSectionFieldsArgs): SectionFieldRenderResult {
  const schema = descriptor?.schema;
  const contentType = schema?.contentType ?? (section?.contentType as string) ?? "RichText";

  if (contentType === "Image") {
    const container = document.createElement("div");
    container.className = "bugence-editor-image";

    const imageField = schema?.fields.find((field) => field.id === "image") ?? FALLBACK_IMAGE_FIELD;
    const altField = schema?.fields.find((field) => field.id === "mediaAltText") ?? FALLBACK_ALT_FIELD;

    const preview = document.createElement("img");
    preview.className = "bugence-editor-image__preview";
    preview.alt = "Image preview";
    preview.dataset.bugenceIgnore = "true";

    const uploadRow = document.createElement("div");
    uploadRow.className = "bugence-editor-image__row";

    const fileInput = document.createElement("input");
    fileInput.type = "file";
    if (imageField.accept?.length) {
      fileInput.accept = imageField.accept.join(",");
    } else {
      fileInput.accept = "image/*";
    }
    fileInput.className = "bugence-editor-image__file";
    fileInput.dataset.bugenceIgnore = "true";

    const fileLabel = document.createElement("label");
    fileLabel.className = "bugence-editor-image__label";
    fileLabel.textContent = imageField.label ?? "Image";
    fileLabel.appendChild(fileInput);

    uploadRow.appendChild(preview);
    uploadRow.appendChild(fileLabel);

    const altWrapper = document.createElement("div");
    altWrapper.className = "bugence-editor-image__alt";

    const altLabel = document.createElement("label");
    altLabel.textContent = altField.label ?? "Alt text";
    altLabel.setAttribute("for", "bugence-editor-alt");

    const altInput = document.createElement("input");
    altInput.type = "text";
    altInput.id = "bugence-editor-alt";
    altInput.className = "bugence-editor-image__alt-input";
    if (altField.placeholder) {
      altInput.placeholder = altField.placeholder;
    }
    if (typeof altField.maxLength === "number") {
      altInput.maxLength = altField.maxLength;
    }

    altWrapper.appendChild(altLabel);
    altWrapper.appendChild(altInput);

    if (altField.helperText) {
      const hint = document.createElement("p");
      hint.className = "bugence-editor-helper text-xs text-ink/60";
      hint.textContent = altField.helperText;
      altWrapper.appendChild(hint);
    }

    container.appendChild(uploadRow);
    container.appendChild(altWrapper);

    const baselineSrc = (section?.mediaPath ?? (element instanceof HTMLImageElement ? element.src : null)) ?? "";
    if (baselineSrc) {
      preview.src = baselineSrc;
    }

    const baselineAlt = (section?.mediaAltText ?? (element instanceof HTMLImageElement ? element.alt : null) ?? altField.placeholder ?? "").trim();
    if (baselineAlt) {
      altInput.value = baselineAlt;
    }

    return {
      kind: "image",
      container,
      fileInput,
      altInput,
      preview,
      imageField,
      altField,
      sanitizeAlt: (value: string) => {
        const trimmed = value.trim();
        const max = typeof altField.maxLength === "number" ? altField.maxLength : undefined;
        const truncated = typeof max === "number" ? trimmed.slice(0, max) : trimmed;
        return truncated;
      },
      accept: imageField.accept,
      maxFileSizeMB: imageField.maxFileSizeMB
    };
  }

  const field = schema?.fields.find((candidate) => candidate.id === "contentValue") ?? FALLBACK_TEXT_FIELD;
  const surface = document.createElement("div");
  surface.className = "bugence-editor-surface";
  surface.contentEditable = "true";
  surface.dataset.bugenceIgnore = "true";

  if (field.placeholder) {
    surface.setAttribute("data-placeholder", field.placeholder);
  }

  if (typeof field.maxLength === "number") {
    surface.dataset.maxLength = field.maxLength.toString();
  }

  const helperElement = field.helperText
    ? (() => {
        const hint = document.createElement("p");
        hint.className = "bugence-editor-helper text-xs text-ink/60";
        hint.dataset.bugenceIgnore = "true";
        hint.textContent = field.helperText ?? "";
        return hint;
      })()
    : undefined;

  const sanitizeText = (value: string) => {
    const plain = (value ?? "").replace(/\r/g, "");
    const max = typeof field.maxLength === "number" ? field.maxLength : undefined;
    const trimmed = typeof max === "number" ? plain.slice(0, max) : plain;
    const wasTruncated = typeof max === "number" ? plain.length > max : false;
    return {
      storageValue: trimmed,
      html: trimmed,
      plain: trimmed,
      wasTruncated
    };
  };

  const sanitizeHtml = (value: string) => {
    const template = document.createElement("template");
    template.innerHTML = value ?? "";

    const allowedTags = field.allowedTags?.length ? field.allowedTags.map((tag) => tag.toLowerCase()) : BASIC_ALLOWED_TAGS;
    const allowedAttrs = field.allowedAttributes?.length ? field.allowedAttributes.map((attr) => attr.toLowerCase()) : BASIC_ALLOWED_ATTRS;

    const cleanNode = (node: Element) => {
      if (!allowedTags.includes(node.tagName.toLowerCase())) {
        const parent = node.parentNode;
        while (node.firstChild) {
          parent?.insertBefore(node.firstChild, node);
        }
        parent?.removeChild(node);
        return;
      }

      Array.from(node.attributes).forEach((attr) => {
        const attrName = attr.name.toLowerCase();
        if (attrName.startsWith("on")) {
          node.removeAttribute(attr.name);
          return;
        }

        if (attrName === "style") {
          if (!allowedAttrs.includes("style")) {
            node.removeAttribute(attr.name);
            return;
          }

          const cleaned = sanitizeStyleAttribute(attr.value);
          if (cleaned.length > 0) {
            node.setAttribute(attr.name, cleaned);
          } else {
            node.removeAttribute(attr.name);
          }
          return;
        }

        if (!allowedAttrs.includes(attrName)) {
          node.removeAttribute(attr.name);
        }
      });

      Array.from(node.children).forEach((child) => cleanNode(child));
    };

    Array.from(template.content.querySelectorAll("*"))
      .forEach((node) => cleanNode(node as Element));

    const sanitizedHtml = template.innerHTML;
    const plain = template.content.textContent ?? "";
    const max = typeof field.maxLength === "number" ? field.maxLength : undefined;
    const truncatedPlain = typeof max === "number" ? plain.slice(0, max) : plain;
    const wasTruncated = typeof max === "number" ? plain.length > max : false;

    return {
      storageValue: sanitizedHtml,
      html: sanitizedHtml,
      plain: truncatedPlain,
      wasTruncated
    };
  };

  const sanitizeRichText = (value: string) => {
    if (field.sanitizer === "none") {
      return {
        storageValue: value ?? "",
        html: value ?? "",
        plain: (value ?? "").replace(/<[^>]+>/g, " ").trim(),
        wasTruncated: false
      };
    }

    if (field.sanitizer === "strict") {
      return sanitizeHtml(value ?? "");
    }

    return sanitizeHtml(value ?? "");
  };

  return {
    kind: "text",
    surface,
    helperElement,
    helperText: field.helperText,
    toolbarTokens: toToolbarTokens(field),
    fieldSchema: field,
    sanitize: (value: string) => {
      if (field.type === "text") {
        return sanitizeText(normalizeText(value));
      }

      return sanitizeRichText(value);
    }
  };
}

function normalizeText(value: string): string {
  return (value ?? "").replace(/\u00a0/g, " ").replace(/\r/g, "");
}
