export type SetStateAction<State> =
  | Partial<State>
  | ((state: State) => Partial<State> | undefined | null);

export type StoreListener<State> = (state: State) => void;

export interface Store<State> {
  getState(): State;
  setState(action: SetStateAction<State>): void;
  subscribe(listener: StoreListener<State>): () => void;
  reset(): void;
}

export function createStore<State extends Record<string, unknown>>(
  initializer: State | (() => State)
): Store<State> {
  const listeners = new Set<StoreListener<State>>();
  const getInitialState =
    typeof initializer === "function"
      ? (initializer as () => State)
      : () => ({ ...(initializer as State) });

  let state = getInitialState();

  const getState = () => state;

  const setState = (action: SetStateAction<State>) => {
    const partial =
      typeof action === "function"
        ? action(state)
        : action;

    if (!partial) {
      return;
    }

    state = Object.freeze({ ...state, ...partial }) as State;
    listeners.forEach((listener) => listener(state));
  };

  const subscribe = (listener: StoreListener<State>) => {
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  };

  const reset = () => {
    state = Object.freeze({ ...getInitialState() }) as State;
    listeners.forEach((listener) => listener(state));
  };

  // Ensure the initial state is immutable copy.
  state = Object.freeze({ ...state }) as State;

  return {
    getState,
    setState,
    subscribe,
    reset
  };
}

