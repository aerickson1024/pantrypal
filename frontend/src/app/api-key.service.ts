import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';

export const apiKeyHeaderName = 'X-Api-Key';
export const verifyApiKeyUrl = '/api/auth/verify-key';

const storageKey = 'pantrypal.apiKey';
const verifyDelayMs = 500;

export type ApiKeyStatus = 'idle' | 'checking' | 'valid' | 'invalid' | 'error';

@Injectable({ providedIn: 'root' })
export class ApiKeyService {
  private readonly http = inject(HttpClient);
  private verifyTimer: ReturnType<typeof setTimeout> | null = null;
  private verifyRequest: Subscription | null = null;

  readonly key = signal(this.readStoredKey());
  readonly status = signal<ApiKeyStatus>('idle');
  readonly isValid = computed(() => this.status() === 'valid');
  readonly statusMessage = computed(() => {
    switch (this.status()) {
      case 'checking':
        return 'Checking key...';
      case 'valid':
        return 'Key accepted. Receipt uploads, manual entry, and tag editing are unlocked.';
      case 'invalid':
        return 'That key is not recognized.';
      case 'error':
        return 'The key could not be checked. Try again later.';
      default:
        return 'Enter your Gemini API key to unlock receipt uploads, manual entry, and tag editing.';
    }
  });

  constructor() {
    if (this.key()) {
      this.scheduleVerify(0);
    }
  }

  setKey(value: string): void {
    const trimmed = value.trim();
    this.key.set(trimmed);
    this.persist(trimmed);
    this.scheduleVerify(verifyDelayMs);
  }

  clear(): void {
    this.cancelVerify();
    this.key.set('');
    this.persist('');
    this.status.set('idle');
  }

  markInvalid(): void {
    this.status.set('invalid');
  }

  private scheduleVerify(delayMs: number): void {
    this.cancelVerify();
    if (!this.key()) {
      this.status.set('idle');
      return;
    }

    this.status.set('checking');
    this.verifyTimer = setTimeout(() => this.verify(), delayMs);
  }

  private verify(): void {
    this.verifyTimer = null;
    this.verifyRequest = this.http.post(verifyApiKeyUrl, null).subscribe({
      next: () => this.status.set('valid'),
      error: (error: HttpErrorResponse) =>
        this.status.set(error.status === 401 ? 'invalid' : 'error'),
    });
  }

  private cancelVerify(): void {
    if (this.verifyTimer !== null) {
      clearTimeout(this.verifyTimer);
      this.verifyTimer = null;
    }

    this.verifyRequest?.unsubscribe();
    this.verifyRequest = null;
  }

  private readStoredKey(): string {
    try {
      return localStorage.getItem(storageKey)?.trim() ?? '';
    } catch {
      return '';
    }
  }

  private persist(value: string): void {
    try {
      if (value) {
        localStorage.setItem(storageKey, value);
      } else {
        localStorage.removeItem(storageKey);
      }
    } catch {
      // Storage can be unavailable (private mode); the key then lasts for this page load only.
    }
  }
}
