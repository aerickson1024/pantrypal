import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { finalize, Observable, tap } from 'rxjs';

export interface AccountProfile {
  email: string;
  firstName: string;
  lastName: string;
  hasGeminiKey: boolean;
}

export type AuthStatus = 'loading' | 'signedIn' | 'signedOut';

export interface RegisterRequest {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  inviteCode: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly status = signal<AuthStatus>('loading');
  readonly profile = signal<AccountProfile | null>(null);
  readonly isSignedIn = computed(() => this.status() === 'signedIn');

  loadSession(): void {
    this.http.get<AccountProfile>('/api/account/me').subscribe({
      next: profile => this.setProfile(profile),
      error: () => this.setSignedOut(),
    });
  }

  register(request: RegisterRequest): Observable<AccountProfile> {
    return this.http
      .post<AccountProfile>('/api/account/register', request)
      .pipe(tap(profile => this.setProfile(profile)));
  }

  login(request: LoginRequest): Observable<AccountProfile> {
    return this.http
      .post<AccountProfile>('/api/account/login', request)
      .pipe(tap(profile => this.setProfile(profile)));
  }

  logout(): void {
    this.http
      .post<void>('/api/account/logout', null)
      .pipe(finalize(() => this.setSignedOut()))
      .subscribe({ error: () => undefined });
  }

  updateName(firstName: string, lastName: string): Observable<AccountProfile> {
    return this.http
      .put<AccountProfile>('/api/account/me', { firstName, lastName })
      .pipe(tap(profile => this.setProfile(profile)));
  }

  getGeminiApiKey(): Observable<{ apiKey: string | null }> {
    return this.http
      .get<{ apiKey: string | null }>('/api/account/gemini-key')
      .pipe(tap(response => this.setHasGeminiKey(!!response.apiKey)));
  }

  saveGeminiApiKey(apiKey: string): Observable<void> {
    return this.http
      .put<void>('/api/account/gemini-key', { apiKey })
      .pipe(tap(() => this.setHasGeminiKey(true)));
  }

  clearGeminiApiKey(): Observable<void> {
    return this.http
      .delete<void>('/api/account/gemini-key')
      .pipe(tap(() => this.setHasGeminiKey(false)));
  }

  setSignedOut(): void {
    this.profile.set(null);
    this.status.set('signedOut');
  }

  private setProfile(profile: AccountProfile): void {
    this.profile.set(profile);
    this.status.set('signedIn');
  }

  private setHasGeminiKey(hasGeminiKey: boolean): void {
    this.profile.update(profile => (profile ? { ...profile, hasGeminiKey } : profile));
  }
}
