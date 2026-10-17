import { inject, Injectable, signal } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Api, AuthService } from './api';
import { Media } from './models';

interface QueuedUpload { id: number; userId: string; url: string; fields: Record<string, string>; files: { name: string; type: string; blob: Blob }[]; createdAt: number; }

/**
 * Spec 07 H2: photo uploads survive poor connectivity on board. Failed/offline uploads are stored in
 * IndexedDB and retried when the browser comes back online (or on the next app start).
 * ponytail: plain IndexedDB, no background-sync API (Safari lacks it); drafts of posts use localStorage.
 */
@Injectable({ providedIn: 'root' })
export class OfflineQueue {
  private api = inject(Api);
  private auth = inject(AuthService);
  private snack = inject(MatSnackBar);
  readonly pending = signal(0);
  /** Bumped after a flush that sent something; lists reload on it. */
  readonly flushed = signal(0);
  readonly online = signal(typeof navigator === 'undefined' ? true : navigator.onLine);
  private flushing = false;

  constructor() {
    if (typeof window === 'undefined') return;
    this.auth.onLogout = () => this.clearAll();
    window.addEventListener('online', () => { this.online.set(true); void this.flush(); });
    window.addEventListener('offline', () => this.online.set(false));
    void this.refreshCount();
    setTimeout(() => void this.flush(), 5000);
  }

  async enqueue(url: string, files: File[], fields: Record<string, string>): Promise<void> {
    const userId = this.auth.me()?.id;
    if (!userId) return;
    const db = await this.db();
    await tx(db, 'readwrite', store => store.add({ userId, url, fields, files: files.map(f => ({ name: f.name, type: f.type, blob: f })), createdAt: Date.now() } satisfies Omit<QueuedUpload, 'id'>));
    await this.refreshCount();
    this.snack.open('Offline – Upload wird nachgeholt, sobald wieder Verbindung besteht.', 'OK', { duration: 5000 });
  }

  /** Queues a JSON request (logbook entry) that failed for lack of connectivity; sent by flush(). */
  async enqueueJson(method: 'POST' | 'PUT', url: string, body: unknown, label: string): Promise<void> {
    const userId = this.auth.me()?.id;
    if (!userId) return;
    outbox.add({ userId, method, url, body, label, createdAt: Date.now() });
    await this.refreshCount();
    this.snack.open(`Offline – ${label} wird gesendet, sobald wieder Verbindung besteht.`, 'OK', { duration: 5000 });
  }

  /** Retries all queued uploads; resolves with the media created in this pass. */
  async flush(): Promise<Media[]> {
    if (this.flushing || !navigator.onLine) return [];
    this.flushing = true;
    const created: Media[] = [];
    try {
      const userId0 = this.auth.me()?.id;
      const sent = await flushOutbox(userId0, item => new Promise<void>((res, rej) => (item.method === 'PUT' ? this.api.put(item.url, item.body) : this.api.post(item.url, item.body)).subscribe({ next: () => res(), error: rej })));
      if (sent.sent) { this.snack.open(`${sent.sent} Eintrag/Einträge nachträglich gespeichert.`, 'OK', { duration: 4000 }); this.flushed.update(n => n + 1); }
      for (const r of sent.rejected) this.snack.open(`${r.label} wurde vom Server abgelehnt.`, 'OK', { duration: 6000 });
      const db = await this.db();
      const items = await tx<QueuedUpload[]>(db, 'readonly', store => store.getAll());
      const userId = this.auth.me()?.id;
      for (const item of items) {
        if (!userId) break;
        if (item.userId !== userId) { await tx(db, 'readwrite', store => store.delete(item.id)); continue; } // queued by another account on this device
        try {
          const files = item.files.map(f => new File([f.blob], f.name, { type: f.type }));
          const result = await new Promise<Media[]>((res, rej) => this.api.upload<Media[]>(item.url, files, item.fields).subscribe({ next: res, error: rej }));
          created.push(...result);
          await tx(db, 'readwrite', store => store.delete(item.id));
        } catch (e) {
          if (isNetworkError(e)) break; // still offline – keep the rest
          await tx(db, 'readwrite', store => store.delete(item.id)); // server rejected it; do not retry forever
          this.snack.open(`Upload von ${item.files.map(f => f.name).join(', ')} abgelehnt.`, 'OK', { duration: 6000 });
        }
      }
      if (created.length) this.snack.open(`${created.length} Foto(s) nachträglich hochgeladen.`, 'OK', { duration: 4000 });
    } finally {
      this.flushing = false;
      await this.refreshCount();
    }
    return created;
  }

  /** Logout hygiene: service-worker caches, queued uploads and drafts are removed from the device. */
  async clearAll(): Promise<void> {
    try { const db = await this.db(); await tx(db, 'readwrite', store => store.clear()); } catch { /* ignore */ }
    outbox.clear();
    try { for (const k of Object.keys(localStorage)) if (k.startsWith('draft:')) localStorage.removeItem(k); } catch { /* ignore */ }
    try { if ('caches' in window) for (const k of await caches.keys()) if (k.startsWith('ngsw:')) await caches.delete(k); } catch { /* ignore */ }
    await this.refreshCount();
  }

  private async refreshCount(): Promise<void> {
    let n = outbox.count();
    try { const db = await this.db(); n += await tx<number>(db, 'readonly', store => store.count()); } catch { /* no IndexedDB */ }
    this.pending.set(n);
  }

  private db(): Promise<IDBDatabase> {
    return new Promise((res, rej) => {
      const req = indexedDB.open('sms-offline', 1);
      req.onupgradeneeded = () => req.result.createObjectStore('uploads', { keyPath: 'id', autoIncrement: true });
      req.onsuccess = () => res(req.result);
      req.onerror = () => rej(req.error);
    });
  }
}

function tx<T>(db: IDBDatabase, mode: IDBTransactionMode, fn: (store: IDBObjectStore) => IDBRequest): Promise<T> {
  return new Promise((res, rej) => {
    const req = fn(db.transaction('uploads', mode).objectStore('uploads'));
    req.onsuccess = () => res(req.result as T);
    req.onerror = () => rej(req.error);
  });
}

export interface OutboxItem { userId: string; method: 'POST' | 'PUT'; url: string; body: unknown; label: string; createdAt: number; }

/** localStorage outbox for small JSON writes (logbook entries) made while offline. */
const OUTBOX_KEY = 'outbox:json';
export const outbox = {
  key: OUTBOX_KEY,
  list(): OutboxItem[] { try { return JSON.parse(localStorage.getItem(OUTBOX_KEY) ?? '[]') as OutboxItem[]; } catch { return []; } },
  add(item: OutboxItem): void { try { localStorage.setItem(OUTBOX_KEY, JSON.stringify([...outbox.list(), item])); } catch { /* quota */ } },
  save(items: OutboxItem[]): void { try { items.length ? localStorage.setItem(OUTBOX_KEY, JSON.stringify(items)) : localStorage.removeItem(OUTBOX_KEY); } catch { /* ignore */ } },
  count(): number { return outbox.list().length; },
  clear(): void { try { localStorage.removeItem(OUTBOX_KEY); } catch { /* ignore */ } },
};

/**
 * Sends the outbox in order for the signed-in user: items of other accounts are dropped, a network error keeps the
 * rest for later, a server rejection drops the item (it would fail forever) and is reported.
 */
export async function flushOutbox(userId: string | undefined, send: (item: OutboxItem) => Promise<void>): Promise<{ sent: number; rejected: OutboxItem[] }> {
  const items = outbox.list();
  const keep: OutboxItem[] = []; const rejected: OutboxItem[] = []; let sent = 0; let offline = false;
  for (const item of items) {
    if (!userId || item.userId !== userId) continue;
    if (offline) { keep.push(item); continue; }
    try { await send(item); sent++; }
    catch (e) { if (isNetworkError(e)) { offline = true; keep.push(item); } else rejected.push(item); }
  }
  outbox.save(keep);
  return { sent, rejected };
}

export function isNetworkError(e: unknown): boolean {
  const status = (e as { status?: number })?.status;
  return status === 0 || status === undefined || !navigator.onLine;
}

/** Simple localStorage drafts (post editor) – survive reloads and connectivity loss. */
export const drafts = {
  key: (scope: string) => `draft:${scope}`,
  load<T>(scope: string): T | null { try { const v = localStorage.getItem(this.key(scope)); return v ? JSON.parse(v) as T : null; } catch { return null; } },
  save(scope: string, value: unknown): void { try { localStorage.setItem(this.key(scope), JSON.stringify(value)); } catch { /* quota */ } },
  clear(scope: string): void { try { localStorage.removeItem(this.key(scope)); } catch { /* ignore */ } },
};
