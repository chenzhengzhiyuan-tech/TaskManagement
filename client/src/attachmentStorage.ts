const DB_NAME = 'g43-client-files'
const STORE_NAME = 'attachments'
const DB_VERSION = 1

function openDatabase(): Promise<IDBDatabase | null> {
  if (typeof indexedDB === 'undefined') return Promise.resolve(null)
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION)
    request.onupgradeneeded = () => {
      const database = request.result
      if (!database.objectStoreNames.contains(STORE_NAME)) database.createObjectStore(STORE_NAME)
    }
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
  })
}

async function withStore<T>(mode: IDBTransactionMode, action: (store: IDBObjectStore, resolve: (value: T) => void, reject: (reason?: unknown) => void) => void): Promise<T | null> {
  const database = await openDatabase()
  if (!database) return null
  return new Promise<T>((resolve, reject) => {
    const transaction = database.transaction(STORE_NAME, mode)
    action(transaction.objectStore(STORE_NAME), resolve, reject)
    transaction.oncomplete = () => database.close()
    transaction.onerror = () => reject(transaction.error)
  })
}

export async function saveAttachmentBlob(id: string, file: Blob) {
  await withStore<void>('readwrite', (store, resolve, reject) => {
    const request = store.put(file, id)
    request.onsuccess = () => resolve()
    request.onerror = () => reject(request.error)
  })
}

export async function getAttachmentBlob(id: string) {
  return withStore<Blob | undefined>('readonly', (store, resolve, reject) => {
    const request = store.get(id)
    request.onsuccess = () => resolve(request.result as Blob | undefined)
    request.onerror = () => reject(request.error)
  })
}

export async function deleteAttachmentBlobs(ids: string[]) {
  if (!ids.length) return
  await withStore<void>('readwrite', (store, resolve) => {
    ids.forEach((id) => store.delete(id))
    resolve()
  })
}

export async function clearAttachmentBlobs() {
  await withStore<void>('readwrite', (store, resolve, reject) => {
    const request = store.clear()
    request.onsuccess = () => resolve()
    request.onerror = () => reject(request.error)
  })
}
