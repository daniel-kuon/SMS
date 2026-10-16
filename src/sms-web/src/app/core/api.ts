import { HttpClient, HttpErrorResponse, HttpInterceptorFn, HttpParams } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, firstValueFrom, Observable, throwError } from 'rxjs';

/** Adds the CSRF marker header the API requires on state-changing requests. */
export const apiInterceptor: HttpInterceptorFn = (req, next) => {
  const snack = inject(MatSnackBar);
  const auth = inject(AuthService);
  const withHeader = req.url.startsWith('/api') ? req.clone({ setHeaders: { 'X-Requested-With': 'XMLHttpRequest' } }) : req;
  return next(withHeader).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 401 && auth.me() !== null) auth.me.set(null);
      if (err.status >= 500) snack.open('Serverfehler – bitte später erneut versuchen.', 'OK', { duration: 5000 });
      return throwError(() => err);
    }),
  );
};

export interface Me {
  id: string; email: string; emailConfirmed: boolean; displayName: string; language: string; isAdmin: boolean;
  defaultContentVisibility: Visibility; avatarUrl: string | null;
}
export type Visibility = 'Private' | 'Crew' | 'Public';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);
  /** null = anonymous, undefined = not loaded yet */
  readonly me = signal<Me | null | undefined>(undefined);

  async load(): Promise<Me | null> {
    try {
      const me = await firstValueFrom(this.http.get<Me>('/api/auth/me'));
      this.me.set(me);
      return me;
    } catch {
      this.me.set(null);
      return null;
    }
  }

  async login(email: string, password: string): Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/login', { email, password, rememberMe: true }));
    await this.load();
  }

  /** Called by OfflineQueue so logout can wipe device-local data without a circular import. */
  onLogout: (() => Promise<void>) | null = null;

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/logout', {}));
    this.me.set(null);
    await this.onLogout?.();
    await this.router.navigateByUrl('/');
  }
}

/** Thin typed HTTP helper used by feature components. */
@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);
  get<T>(url: string, params?: Record<string, string | number | boolean | undefined | null>): Observable<T> {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(params ?? {})) if (v !== undefined && v !== null && v !== '') p = p.set(k, String(v));
    return this.http.get<T>(url, { params: p });
  }
  post<T>(url: string, body: unknown): Observable<T> { return this.http.post<T>(url, body); }
  put<T>(url: string, body: unknown): Observable<T> { return this.http.put<T>(url, body); }
  delete<T>(url: string): Observable<T> { return this.http.delete<T>(url); }
  upload<T>(url: string, files: File[], fields: Record<string, string> = {}): Observable<T> {
    const form = new FormData();
    for (const f of files) form.append('files', f, f.name);
    for (const [k, v] of Object.entries(fields)) form.append(k, v);
    return this.http.post<T>(url, form);
  }
}

/** Extracts a readable message from an API error (ProblemDetails / ValidationProblem). */
export function errorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const e = err.error;
    if (e?.errors) return Object.values(e.errors as Record<string, string[]>).flat().join(' ');
    if (e?.title) return e.title;
    if (err.status === 401) return 'Bitte anmelden.';
    if (err.status === 403) return 'Keine Berechtigung.';
    return `Fehler ${err.status}`;
  }
  return String(err);
}
