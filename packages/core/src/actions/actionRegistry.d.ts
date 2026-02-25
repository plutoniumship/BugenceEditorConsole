import type { ActionContext, ActionMeta, ActionModule, ActionRegistration, ActionResult } from "./types";
export declare function registerAction(registration: ActionRegistration): void;
export declare function registerActions(registrations: ActionRegistration[]): void;
export declare function unregisterAction(id: string): boolean;
export declare function getActionMeta(id: string): ActionMeta | null;
export declare function listActionMetas(filter?: (meta: ActionMeta) => boolean): ActionMeta[];
export declare function loadActionModule(id: string): Promise<ActionModule>;
export declare function executeAction<TParams = unknown, TContext extends ActionContext = ActionContext>(id: string, context: TContext, params?: TParams): Promise<ActionResult>;
//# sourceMappingURL=actionRegistry.d.ts.map