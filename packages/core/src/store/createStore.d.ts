export type SetStateAction<State> = Partial<State> | ((state: State) => Partial<State> | undefined | null);
export type StoreListener<State> = (state: State) => void;
export interface Store<State> {
    getState(): State;
    setState(action: SetStateAction<State>): void;
    subscribe(listener: StoreListener<State>): () => void;
    reset(): void;
}
export declare function createStore<State extends Record<string, unknown>>(initializer: State | (() => State)): Store<State>;
//# sourceMappingURL=createStore.d.ts.map