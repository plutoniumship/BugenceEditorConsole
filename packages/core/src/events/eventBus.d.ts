import type { EventBus } from "../types";
interface EventBusOptions {
    busId?: string;
    useWindowFallback?: boolean;
    logger?: (level: "debug" | "error", message: string, context?: unknown) => void;
}
export declare function createEventBus<TTopic extends string = string, TPayload = unknown>(name: string, options?: EventBusOptions): EventBus<TTopic, TPayload>;
export {};
//# sourceMappingURL=eventBus.d.ts.map