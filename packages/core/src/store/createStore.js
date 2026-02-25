export function createStore(initializer) {
    const listeners = new Set();
    const getInitialState = typeof initializer === "function"
        ? initializer
        : () => ({ ...initializer });
    let state = getInitialState();
    const getState = () => state;
    const setState = (action) => {
        const partial = typeof action === "function"
            ? action(state)
            : action;
        if (!partial) {
            return;
        }
        state = Object.freeze({ ...state, ...partial });
        listeners.forEach((listener) => listener(state));
    };
    const subscribe = (listener) => {
        listeners.add(listener);
        return () => {
            listeners.delete(listener);
        };
    };
    const reset = () => {
        state = Object.freeze({ ...getInitialState() });
        listeners.forEach((listener) => listener(state));
    };
    // Ensure the initial state is immutable copy.
    state = Object.freeze({ ...state });
    return {
        getState,
        setState,
        subscribe,
        reset
    };
}
//# sourceMappingURL=createStore.js.map