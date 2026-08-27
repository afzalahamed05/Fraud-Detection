import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ApiService, API_BASE_URL } from './api.service';

describe('ApiService', () => {
  let service: ApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [ApiService, provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(ApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getTransactions requests the correct URL with pagination params', () => {
    service.getTransactions(2, 10).subscribe();

    const req = httpMock.expectOne(
      (r) => r.url === `${API_BASE_URL}/transactions` && r.params.get('page') === '2' && r.params.get('pageSize') === '10'
    );
    expect(req.request.method).toBe('GET');
    req.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
  });

  it('getTransactions includes filter params when provided, omitting empty ones', () => {
    service.getTransactions(1, 25, { status: 'Flagged', search: '' }).subscribe();

    const req = httpMock.expectOne(
      (r) => r.url === `${API_BASE_URL}/transactions` && r.params.get('status') === 'Flagged'
    );
    expect(req.request.params.has('search')).toBeFalse();
    req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  });

  it('createTransaction posts the payload to /transactions', () => {
    const payload = {
      accountId: 'acct-1',
      merchantName: 'Test',
      merchantCategory: 'Retail',
      amount: 10,
      currency: 'USD',
      countryCode: 'US'
    };

    service.createTransaction(payload).subscribe();

    const req = httpMock.expectOne(`${API_BASE_URL}/transactions`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush({ ...payload, id: '1', status: 'Pending', occurredAtUtc: '', alertCount: 0 });
  });

  it('login posts username/password to /auth/login', () => {
    service.login('admin', 'admin123').subscribe();

    const req = httpMock.expectOne(`${API_BASE_URL}/auth/login`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ username: 'admin', password: 'admin123' });
    req.flush({ token: 'abc', expiresAtUtc: '', username: 'admin' });
  });

  it('getTopTriggers passes the limit query param', () => {
    service.getTopTriggers(3).subscribe();

    const req = httpMock.expectOne((r) => r.url === `${API_BASE_URL}/fraud-alerts/top-triggers` && r.params.get('limit') === '3');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });
});
