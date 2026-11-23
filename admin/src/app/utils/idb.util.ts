// Tiny promise wrapper over IndexedDB: enough for the POS offline copy and sale queue, no library needed.
// Every function rejects (never throws synchronously) so callers can fall back when storage is blocked
// (private windows, some in-app browsers).

export interface IdbStoreSpec { name: string; keyPath: string; }

export function openDb(dbName: string, stores: IdbStoreSpec[], version = 1): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === 'undefined') { reject(new Error('IndexedDB is not available')); return; }
    const req = indexedDB.open(dbName, version);
    req.onupgradeneeded = () => {
      const db = req.result;
      for (const s of stores) if (!db.objectStoreNames.contains(s.name)) db.createObjectStore(s.name, { keyPath: s.keyPath });
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error ?? new Error('Could not open the local database'));
    req.onblocked = () => reject(new Error('The local database is blocked by another tab'));
  });
}

function run<T>(db: IDBDatabase, store: string, mode: IDBTransactionMode, work: (s: IDBObjectStore) => IDBRequest | void): Promise<T> {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(store, mode);
    let result: any;
    const req = work(tx.objectStore(store));
    if (req) req.onsuccess = () => { result = req.result; };
    tx.oncomplete = () => resolve(result as T);
    tx.onerror = () => reject(tx.error ?? new Error('Local database error'));
    tx.onabort = () => reject(tx.error ?? new Error('Local database transaction aborted'));
  });
}

export const idbGetAll = <T>(db: IDBDatabase, store: string) => run<T[]>(db, store, 'readonly', s => s.getAll());
export const idbGet = <T>(db: IDBDatabase, store: string, key: IDBValidKey) => run<T | undefined>(db, store, 'readonly', s => s.get(key));
export const idbPut = (db: IDBDatabase, store: string, value: unknown) => run<void>(db, store, 'readwrite', s => s.put(value));
export const idbDelete = (db: IDBDatabase, store: string, key: IDBValidKey) => run<void>(db, store, 'readwrite', s => s.delete(key));

/** Replaces the whole contents of a store in ONE transaction, so a half-written catalogue is never visible. */
export function idbReplaceAll(db: IDBDatabase, store: string, values: unknown[]): Promise<void> {
  return run<void>(db, store, 'readwrite', s => { s.clear(); for (const v of values) s.put(v); });
}
