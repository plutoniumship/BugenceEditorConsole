import type { SnapshotEnvelope, Uuid } from "@bugence/core";

interface BaselineRecord {
  snapshots: SnapshotEnvelope[];
  updatedAtUtc: string;
  version: number;
}

type BaselineStore = Record<string, BaselineRecord>;

const STORAGE_KEY = "bugence:canvas:baseline";
const memoryStore = new Map<string, BaselineRecord>();

function getStorage(): Storage | null {
  if (typeof window === "undefined") {
    return null;
  }

  try {
    return window.sessionStorage;
  } catch {
    return null;
  }
}

function readStore(): BaselineStore {
  const storage = getStorage();
  if (!storage) {
    return {};
  }

  try {
    const raw = storage.getItem(STORAGE_KEY);
    if (!raw) {
      return {};
    }

    const parsed = JSON.parse(raw) as BaselineStore;
    return parsed ?? {};
  } catch {
    return {};
  }
}

function writeStore(store: BaselineStore) {
  const storage = getStorage();
  if (!storage) {
    return;
  }

  try {
    storage.setItem(STORAGE_KEY, JSON.stringify(store));
  } catch {
    // ignore failures (quota, private mode, etc.)
  }
}

function toMemoryRecord(record: BaselineRecord | undefined): BaselineRecord | undefined {
  if (!record) {
    return undefined;
  }

  return {
    snapshots: record.snapshots,
    updatedAtUtc: record.updatedAtUtc,
    version: record.version
  };
}

export function loadBaseline(pageId: Uuid): BaselineRecord | undefined {
  if (memoryStore.has(pageId)) {
    return toMemoryRecord(memoryStore.get(pageId));
  }

  const store = readStore();
  const record = store[pageId];
  if (record) {
    memoryStore.set(pageId, record);
  }

  return toMemoryRecord(record);
}

export function saveBaseline(
  pageId: Uuid,
  snapshots: SnapshotEnvelope[],
  version: number
): BaselineRecord {
  const record: BaselineRecord = {
    snapshots,
    updatedAtUtc: new Date().toISOString(),
    version
  };

  memoryStore.set(pageId, record);

  const store = readStore();
  store[pageId] = record;
  writeStore(store);

  return record;
}

export function updateBaselineSnapshot(
  pageId: Uuid,
  snapshot: SnapshotEnvelope
): BaselineRecord {
  const current = loadBaseline(pageId) ?? {
    snapshots: [],
    updatedAtUtc: new Date().toISOString(),
    version: 0
  };

  const snapshotMap = new Map(current.snapshots.map((entry) => [entry.sectionId, entry]));
  snapshotMap.set(snapshot.sectionId, snapshot);

  return saveBaseline(pageId, Array.from(snapshotMap.values()), current.version + 1);
}

export function removeBaselineSnapshot(pageId: Uuid, sectionId: Uuid): BaselineRecord {
  const current = loadBaseline(pageId);
  const snapshots =
    current?.snapshots.filter((entry) => entry.sectionId !== sectionId) ?? [];
  return saveBaseline(pageId, snapshots, (current?.version ?? 0) + 1);
}

export function clearBaseline(pageId?: Uuid) {
  if (!pageId) {
    memoryStore.clear();
    writeStore({});
    return;
  }

  memoryStore.delete(pageId);
  const store = readStore();
  delete store[pageId];
  writeStore(store);
}

