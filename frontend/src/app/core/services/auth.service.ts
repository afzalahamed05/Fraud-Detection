import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';

const TOKEN_KEY = 'fraud_detection_token';
const USERNAME_KEY = 'fraud_detection_username';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);

  readonly token = signal<string | null>(this.readStoredToken());
  readonly username = signal<string | null>(localStorage.getItem(USERNAME_KEY));
  readonly isAuthenticated = computed(() => this.token() !== null);

  private readStoredToken(): string | null {
    try {
      return localStorage.getItem(TOKEN_KEY);
    } catch {
      return null;
    }
  }

  login(username: string, password: string) {
    return this.api.login(username, password).pipe();
  }

  setSession(token: string, username: string): void {
    localStorage.setItem(TOKEN_KEY, token);
    localStorage.setItem(USERNAME_KEY, username);
    this.token.set(token);
    this.username.set(username);
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USERNAME_KEY);
    this.token.set(null);
    this.username.set(null);
  }
}
