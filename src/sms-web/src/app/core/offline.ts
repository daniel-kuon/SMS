import { inject, Injectable, signal } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Api } from './api';
import { Media } from './models';

interface QueuedUpload { id: number; url: string; fields: Record<string, string>; files: { name: string; type: string; blob: Blob }[]; createdAt: number; }

/**
 * Spec 07 H2: photo uploads survive poor connectivity on board. Failed/offline uploads are stored in
 * IndexedDB and retried when the browser comes back online (or on the next app start).
 * ponytail: plain IndexedDB, no background-sync API (Safari lacks it); drafts of posts use localStorage.
 */
@Injectable({ providedIn: 'root' })
export class OfflineQueue {
  private api = inject(Api);
  private snack = inject(MatSnackBar);
  readonly pending = signal(0);
  readonly online = signal(typeof navigator === 'undefined' ? true : navigator.onLine);
  private flushing = false;

  constructor() {
    if (typeof window === 'undefined') return;
    window.addEventListener('online', () => { this.online.set(true); void this.flush(); });
    window.addEventListener('offline', () => this.online.set(false));
    void this.refreshCount();
    setTimeout(() => void this.flush(), 5000);
  }

  async enqueue(url: string, files: File[], fields: Record<string, string>): Promise<void> {
    const db = await this.db();
    await tx(db, 'readwrite', store => store.add({ url, fields, files: files.map(f => ({ name: f.name, type: f.type, blob: f })), createdAt: Date.now() } satisfies Omit<QueuedUpload, 'id'>));
    await this.refreshCount();
    this.snack.open('Offline – Upload wird nachgeholt, sobald wieder Verbindung besteht.', 'OK', { duration: 5000 });
  }

  /** Retries all queued uploads; resolves with the media created in this pass. */
  async flush(): Promise<Media[]> {
    if (this.flushing || !navigator.onLine) return [];
    this.flushing = true;
    const created: Media[] = [];
    try {
      const db = await this.db();
      const items = await tx<QueuedUpload[]>(db, 'readonly', store => store.getAll());
      for (const item of items) {
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

  private async refreshCount(): Promise<void> {
    try { const db = await this.db(); this.pending.set(await tx<number>(db, 'readonly', store => store.count())); } catch { this.pending.set(0); }
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
