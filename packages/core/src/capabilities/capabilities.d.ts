export declare function setCapability(id: string, enabled: boolean): void;
export declare function enableCapability(id: string): void;
export declare function disableCapability(id: string): void;
export declare function configureCapabilities(capabilities: Record<string, boolean | string | number> | string[]): void;
export declare function hasCapability(id: string): boolean;
export declare function listCapabilities(): Array<{
    id: string;
    enabled: boolean;
}>;
export declare function clearCapabilities(): void;
//# sourceMappingURL=capabilities.d.ts.map