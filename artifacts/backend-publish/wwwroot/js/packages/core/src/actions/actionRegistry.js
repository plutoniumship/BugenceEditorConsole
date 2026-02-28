import { hasCapability } from "../capabilities/capabilities";
const actions = new Map();
function normaliseId(id) {
    return id.trim();
}
function assertRegistration(registration) {
    if (!registration?.meta?.id) {
        throw new Error("Action registration must include a meta.id value.");
    }
    if (typeof registration.load !== "function") {
        throw new Error(`Action '${registration.meta.id}' must provide a loader function.`);
    }
}
function isCapabilitySatisfied(meta) {
    if (!meta.capabilities?.length) {
        return true;
    }
    return meta.capabilities.every((capability) => hasCapability(capability));
}
async function resolveModule(entry) {
    if (entry.cachedModule && entry.registration.cache !== false) {
        return entry.cachedModule;
    }
    const loaded = await Promise.resolve(entry.registration.load());
    if (!loaded || typeof loaded.execute !== "function") {
        throw new Error(`Action '${entry.registration.meta.id}' did not return a module with an execute function.`);
    }
    if (entry.registration.cache !== false) {
        entry.cachedModule = loaded;
    }
    return loaded;
}
export function registerAction(registration) {
    assertRegistration(registration);
    const id = normaliseId(registration.meta.id);
    if (actions.has(id)) {
        throw new Error(`Action '${id}' is already registered.`);
    }
    actions.set(id, {
        registration
    });
}
export function registerActions(registrations) {
    registrations.forEach(registerAction);
}
export function unregisterAction(id) {
    return actions.delete(normaliseId(id));
}
export function getActionMeta(id) {
    const entry = actions.get(normaliseId(id));
    return entry ? entry.registration.meta : null;
}
export function listActionMetas(filter) {
    const metas = Array.from(actions.values()).map((entry) => entry.registration.meta);
    return typeof filter === "function" ? metas.filter(filter) : metas;
}
export async function loadActionModule(id) {
    const entry = actions.get(normaliseId(id));
    if (!entry) {
        throw new Error(`Action '${id}' is not registered.`);
    }
    if (!isCapabilitySatisfied(entry.registration.meta)) {
        throw new Error(`Action '${id}' is disabled by capability flags.`);
    }
    return resolveModule(entry);
}
export async function executeAction(id, context, params) {
    const entry = actions.get(normaliseId(id));
    if (!entry) {
        return {
            status: "error",
            message: `Action '${id}' is not registered.`,
            timestamp: new Date().toISOString()
        };
    }
    const meta = entry.registration.meta;
    if (!isCapabilitySatisfied(meta)) {
        return {
            status: "skipped",
            message: `Action '${id}' is disabled by capability flags.`,
            timestamp: new Date().toISOString()
        };
    }
    try {
        const module = await resolveModule(entry);
        if (module.canExecute) {
            const allowed = await Promise.resolve(module.canExecute(context));
            if (!allowed) {
                return {
                    status: "skipped",
                    message: `Action '${id}' cannot run in the current context.`,
                    timestamp: new Date().toISOString()
                };
            }
        }
        const result = await Promise.resolve(module.execute(context, params));
        if (result && typeof result === "object") {
            return {
                timestamp: result.timestamp ?? new Date().toISOString(),
                status: result.status,
                message: result.message,
                data: result.data
            };
        }
        return {
            status: "success",
            timestamp: new Date().toISOString()
        };
    }
    catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        return {
            status: "error",
            message,
            data: { error },
            timestamp: new Date().toISOString()
        };
    }
}
