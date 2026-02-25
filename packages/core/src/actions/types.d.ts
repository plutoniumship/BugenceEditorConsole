export type ActionStatus = "success" | "error" | "skipped";
export interface ActionResult<TData = unknown> {
    status: ActionStatus;
    message?: string;
    data?: TData;
    timestamp?: string;
}
export interface ActionContext extends Record<string, unknown> {
    pageId?: string;
    sectionId?: string;
    surface?: "canvas" | "dashboard" | "insights" | string;
}
export interface ActionMeta {
    id: string;
    title: string;
    description?: string;
    category?: string;
    icon?: string;
    order?: number;
    capabilities?: string[];
    tags?: string[];
    keywords?: string[];
    requiresSelection?: boolean;
}
export interface ActionModule<TParams = unknown, TContext extends ActionContext = ActionContext> {
    execute(context: TContext, params?: TParams): Promise<ActionResult> | ActionResult;
    canExecute?(context: TContext): Promise<boolean> | boolean;
}
export type ActionLoader<TParams = unknown, TContext extends ActionContext = ActionContext> = () => Promise<ActionModule<TParams, TContext>> | ActionModule<TParams, TContext>;
export interface ActionRegistration<TParams = unknown, TContext extends ActionContext = ActionContext> {
    meta: ActionMeta;
    load: ActionLoader<TParams, TContext>;
    cache?: boolean;
}
//# sourceMappingURL=types.d.ts.map