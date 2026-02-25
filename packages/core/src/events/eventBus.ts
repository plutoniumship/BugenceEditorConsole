import type { EventBus, EventEnvelope, EventListener, IsoDateString } from "../types";

interface EventBusOptions {
  busId?: string;
  useWindowFallback?: boolean;
  logger?: (level: "debug" | "error", message: string, context?: unknown) => void;
}

interface MemoryChannelListener {
  busId: string;
  handler: (event: EventEnvelope) => void;
}

const memoryChannels = new Map<string, Set<MemoryChannelListener>>();

function getMemoryChannel(name: string) {
  let channel = memoryChannels.get(name);
  if (!channel) {
    channel = new Set<MemoryChannelListener>();
    memoryChannels.set(name, channel);
  }
  return channel;
}

function generateId(): string {
  const cryptoRef = (globalThis as { crypto?: Crypto }).crypto;
  if (cryptoRef && typeof cryptoRef.randomUUID === "function") {
    return cryptoRef.randomUUID();
  }

  return `evt_${Math.random().toString(16).slice(2)}${Date.now().toString(16)}`;
}

function nowIso(): IsoDateString {
  return new Date().toISOString();
}

function isBroadcastChannelAvailable(): boolean {
  return typeof globalThis.BroadcastChannel === "function";
}

function isWindowAvailable(): boolean {
  return typeof window !== "undefined" && typeof window.postMessage === "function";
}

export function createEventBus<TTopic extends string = string, TPayload = unknown>(
  name: string,
  options: EventBusOptions = {}
): EventBus<TTopic, TPayload> {
  const busId = options.busId ?? generateId();
  const listeners = new Set<EventListener<TTopic, TPayload>>();

  let broadcastChannel: BroadcastChannel | null = null;
  let windowListener: ((event: MessageEvent) => void) | null = null;
  let memoryUnsubscribe: (() => void) | null = null;

  const log = (level: "debug" | "error", message: string, context?: unknown) => {
    if (!options.logger) {
      return;
    }
    try {
      options.logger(level, message, context);
    } catch {
      // no-op: logging must never throw
    }
  };

  const dispatchLocal = (envelope: EventEnvelope<TTopic, TPayload>) => {
    listeners.forEach((listener) => {
      try {
        listener(envelope);
      } catch (error) {
        log("error", "Event bus listener threw", { error, envelope });
      }
    });
  };

  const handleRemote = (envelope: EventEnvelope) => {
    if (!envelope || typeof envelope !== "object") {
      return;
    }

    if (envelope.source === busId) {
      return;
    }

    dispatchLocal(envelope as EventEnvelope<TTopic, TPayload>);
  };

  if (isBroadcastChannelAvailable()) {
    broadcastChannel = new BroadcastChannel(name);
    broadcastChannel.addEventListener("message", (event: MessageEvent<EventEnvelope>) => {
      handleRemote(event.data);
    });
  } else if (options.useWindowFallback !== false && isWindowAvailable()) {
    windowListener = (event: MessageEvent) => {
      const data = event.data;
      if (!data || typeof data !== "object" || data.__bugenceBusName !== name) {
        return;
      }

      handleRemote(data.envelope as EventEnvelope);
    };

    window.addEventListener("message", windowListener);
  } else {
    const channel = getMemoryChannel(name);
    const entry: MemoryChannelListener = {
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

  const publish = (
    topic: TTopic,
    payload: TPayload
  ): EventEnvelope<TTopic, TPayload> => {
    const envelope: EventEnvelope<TTopic, TPayload> = {
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
      } catch (error) {
        log("error", "Failed to publish via BroadcastChannel", { error, envelope });
      }
    } else if (windowListener && isWindowAvailable()) {
      window.postMessage(
        {
          __bugenceBusName: name,
          envelope
        },
        "*"
      );
    } else {
      const channel = memoryChannels.get(name);
      if (channel) {
        channel.forEach((entry) => {
          if (entry.busId === busId) {
            return;
          }
          try {
            entry.handler(envelope);
          } catch (error) {
            log("error", "Memory event handler threw", { error, envelope });
          }
        });
      }
    }

    return envelope;
  };

  const subscribe = (listener: EventListener<TTopic, TPayload>) => {
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
      } catch {
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
