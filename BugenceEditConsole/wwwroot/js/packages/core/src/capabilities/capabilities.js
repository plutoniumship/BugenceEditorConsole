const capabilityFlags = new Map();
function normaliseId(id) {
    return id.trim().toLowerCase();
}
function coerceBoolean(value) {
    if (typeof value === "boolean") {
        return value;
    }
    if (typeof value === "number") {
        return value !== 0;
    }
    if (typeof value === "string") {
        const trimmed = value.trim().toLowerCase();
        if (trimmed === "false" || trimmed === "0" || trimmed === "off" || trimmed === "no") {
            return false;
        }
        if (trimmed === "true" || trimmed === "1" || trimmed === "on" || trimmed === "yes") {
            return true;
        }
    }
    return Boolean(value);
}
export function setCapability(id, enabled) {
    capabilityFlags.set(normaliseId(id), enabled);
}
export function enableCapability(id) {
    setCapability(id, true);
}
export function disableCapability(id) {
    setCapability(id, false);
}
export function configureCapabilities(capabilities) {
    if (Array.isArray(capabilities)) {
        capabilities.forEach((id) => enableCapability(id));
        return;
    }
    Object.entries(capabilities).forEach(([id, value]) => {
        setCapability(id, coerceBoolean(value));
    });
}
export function hasCapability(id) {
    const normalised = normaliseId(id);
    if (capabilityFlags.has(normalised)) {
        return capabilityFlags.get(normalised) === true;
    }
    if (capabilityFlags.has("*")) {
        return capabilityFlags.get("*") === true;
    }
    return true;
}
export function listCapabilities() {
    return Array.from(capabilityFlags.entries()).map(([id, enabled]) => ({
        id,
        enabled
    }));
}
export function clearCapabilities() {
    capabilityFlags.clear();
}
