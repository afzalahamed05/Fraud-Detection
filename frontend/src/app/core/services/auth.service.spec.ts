import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [AuthService, provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(AuthService);
  });

  afterEach(() => localStorage.clear());

  it('starts unauthenticated when no token is stored', () => {
    expect(service.isAuthenticated()).toBeFalse();
    expect(service.token()).toBeNull();
  });

  it('setSession stores the token and marks the user authenticated', () => {
    service.setSession('fake-jwt', 'admin');

    expect(service.isAuthenticated()).toBeTrue();
    expect(service.token()).toBe('fake-jwt');
    expect(service.username()).toBe('admin');
    expect(localStorage.getItem('fraud_detection_token')).toBe('fake-jwt');
  });

  it('logout clears the session', () => {
    service.setSession('fake-jwt', 'admin');
    service.logout();

    expect(service.isAuthenticated()).toBeFalse();
    expect(service.token()).toBeNull();
    expect(localStorage.getItem('fraud_detection_token')).toBeNull();
  });

  it('a fresh AuthService instance picks up a token already in localStorage', () => {
    localStorage.setItem('fraud_detection_token', 'persisted-jwt');
    localStorage.setItem('fraud_detection_username', 'admin');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [AuthService, provideHttpClient(), provideHttpClientTesting()]
    });
    const rehydrated = TestBed.inject(AuthService);

    expect(rehydrated.isAuthenticated()).toBeTrue();
    expect(rehydrated.username()).toBe('admin');
  });
});
