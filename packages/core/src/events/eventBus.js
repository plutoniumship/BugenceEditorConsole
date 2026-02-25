const memoryChannels = new Map();
function getMemoryChannel(name) {
    let channel = memoryChannels.get(name);
    if (!channel) {
        channel = new Set();
        memoryChannels.set(name, channel);
    }
    return channel;
}
function generateId() {
    const cryptoRef = globalThis.crypto;
    if (cryptoRef && typeof cryptoRef.randomUUID === "function") {
        return cryptoRef.randomUUID();
    }
    return `evt_${Math.random().toString(16).slice(2)}${Date.now().toString(16)}`;
}
function nowIso() {
    return new Date().toISOString();
}
function isBroadcastChannelAvailable() {
    return typeof globalThis.BroadcastChannel === "function";
}
function isWindowAvailable() {
    return typeof window !== "undefined" && typeof window.postMessage === "function";
}
export function createEventBus(name, options = {}) {
    const busId = options.busId ?? generateId();
    const listeners = new Set();
    let broadcastChannel = null;
    let windowListener = null;
    let memoryUnsubscribe = null;
    const log = (level, message, context) => {
        if (!options.logger) {
            return;
        }
        try {
            options.logger(level, message, context);
        }
        catch {
            // no-op: logging must never throw
        }
    };
    const dispatchLocal = (envelope) => {
        listeners.forEach((listener) => {
            try {
                listener(envelope);
            }
            catch (error) {
                log("error", "Event bus listener threw", { error, envelope });
            }
        });
    };
    const handleRemote = (envelope) => {
        if (!envelope || typeof envelope !== "object") {
            return;
        }
        if (envelope.source === busId) {
            return;
        }
        dispatchLocal(envelope);
    };
    if (isBroadcastChannelAvailable()) {
        broadcastChannel = new BroadcastChannel(name);
        broadcastChannel.addEventListener("message", (event) => {
            handleRemote(event.data);
        });
    }
    else if (options.useWindowFallback !== false && isWindowAvailable()) {
        windowListener = (event) => {
            const data = event.data;
            if (!data || typeof data !== "object" || data.__bugenceBusName !== name) {
                return;
            }
            handleRemote(data.envelope);
        };
        window.addEventListener("message", windowListener);
    }
    else {
        const channel = getMemoryChannel(name);
        const entry = {
            busId,
            handler: handleRemote
        };
        channel.add(entry);
        memoryUnsubscribe = () => {
            channel.delete(entry);
            if (channel.size === 0) {
                memoryChannels.delete(name);
            }
        };
    }
    const publish = (topic, payload) => {
        const envelope = {
            id: generateId(),
            topic,
            payload,
            timestamp: nowIso(),
            source: busId
        };
        dispatchLocal(envelope);
        if (broadcastChannel) {
            try {
                broadcastChannel.postMessage(envelope);
            }
            catch (error) {
                log("error", "Failed to publish via BroadcastChannel", { error, envelope });
            }
        }
        else if (windowListener && isWindowAvailable()) {
            window.postMessage({
                __bugenceBusName: name,
                envelope
            }, "*");
        }
        else {
            const channel = memoryChannels.get(name);
            if (channel) {
                channel.forEach((entry) => {
                    if (entry.busId === busId) {
                        return;
                    }
                    try {
                        entry.handler(envelope);
                    }
                    catch (error) {
                        log("error", "Memory event handler threw", { error, envelope });
                    }
                });
            }
        }
        return envelope;
    };
    const subscribe = (listener) => {
        listeners.add(listener);
        return () => {
            listeners.delete(listener);
        };
    };
    const close = () => {
        listeners.clear();
        if (broadcastChannel) {
            try {
                broadcastChannel.close();
            }
            catch {
                // ignore
            }
            broadcastChannel = null;
        }
        if (windowListener && isWindowAvailable()) {
            window.removeEventListener("message", windowListener);
        }
        windowListener = null;
        if (memoryUnsubscribe) {
            memoryUnsubscribe();
            memoryUnsubscribe = null;
        }
    };
    return {
        publish,
        subscribe,
        close,
        getId() {
            return busId;
        }
    };
}
//# sourceMappingURL=eventBus.js.map