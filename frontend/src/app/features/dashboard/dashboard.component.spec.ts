import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { DashboardComponent } from './dashboard.component';
import { API_BASE_URL } from '../../core/services/api.service';

describe('DashboardComponent', () => {
  let fixture: ComponentFixture<DashboardComponent>;
  let component: DashboardComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function flushInitialRequests(): void {
    httpMock.expectOne(`${API_BASE_URL}/dashboard/stats`).flush({
      totalTransactions: 100,
      totalAlerts: 10,
      openAlerts: 5,
      fraudRate: 10,
      totalAmount: 1000,
      flaggedAmount: 100,
      alertsBySeverity: { Low: 0, Medium: 4, High: 5, Critical: 1 }
    });
    httpMock.expectOne((r) => r.url === `${API_BASE_URL}/transactions`).flush({ items: [], page: 1, pageSize: 10, totalCount: 0 });
    httpMock.expectOne((r) => r.url === `${API_BASE_URL}/fraud-alerts`).flush({ items: [], page: 1, pageSize: 10, totalCount: 0 });
    httpMock.expectOne(`${API_BASE_URL}/health/pipeline`).flush({
      kafkaConnected: true,
      pendingCount: 0,
      unpublishedCount: 0,
      stuckCount: 0,
      failedCount: 0,
      messagesProduced: 0,
      messagesConsumed: 0,
      messagesFailed: 0,
      lastConsumedAtUtc: null,
      avgProcessingLatencyMs: 25
    });
    httpMock.expectOne((r) => r.url === `${API_BASE_URL}/dashboard/trends`).flush([]);
    httpMock.expectOne((r) => r.url === `${API_BASE_URL}/fraud-alerts/top-triggers`).flush([]);
  }

  it('creates and issues the expected initial data requests', () => {
    fixture.detectChanges();
    flushInitialRequests();
    expect(component).toBeTruthy();
  });

  it('computes severity bars scaled against the largest bucket', () => {
    fixture.detectChanges();
    flushInitialRequests();

    const bars = component.severityBars();
    const high = bars.find((b) => b.severity === 'High')!;
    const critical = bars.find((b) => b.severity === 'Critical')!;

    expect(high.count).toBe(5);
    expect(high.pct).toBe(100); // 5 is the max bucket
    expect(critical.pct).toBe(20); // 1 / 5 = 20%
  });

  it('routes to /login instead of simulating when unauthenticated', () => {
    fixture.detectChanges();
    flushInitialRequests();

    const routerSpy = spyOn((component as any).router, 'navigateByUrl');
    component.simulateTransaction();

    expect(routerSpy).toHaveBeenCalledWith('/login');
    httpMock.expectNone(`${API_BASE_URL}/transactions`);
  });
});
