import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AlertFilter,
  CustomerRiskProfile,
  DailyTrend,
  DashboardStats,
  FraudAlert,
  LoginResponse,
  PagedResult,
  PipelineHealth,
  Transaction,
  TransactionFilter,
  TopTrigger
} from '../models/transaction.model';

export const API_BASE_URL = 'http://localhost:5274/api';

function buildParams(base: Record<string, string | number>, filter?: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(base)) {
    params = params.set(key, value);
  }
  if (filter) {
    for (const [key, value] of Object.entries(filter as Record<string, unknown>)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    }
  }
  return params;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  getTransactions(page = 1, pageSize = 25, filter?: TransactionFilter): Observable<PagedResult<Transaction>> {
    const params = buildParams({ page, pageSize }, filter);
    return this.http.get<PagedResult<Transaction>>(`${API_BASE_URL}/transactions`, { params });
  }

  getTransaction(id: string): Observable<Transaction> {
    return this.http.get<Transaction>(`${API_BASE_URL}/transactions/${id}`);
  }

  getFraudAlerts(page = 1, pageSize = 25, filter?: AlertFilter): Observable<PagedResult<FraudAlert>> {
    const params = buildParams({ page, pageSize }, filter);
    return this.http.get<PagedResult<FraudAlert>>(`${API_BASE_URL}/fraud-alerts`, { params });
  }

  getFraudAlert(id: string): Observable<FraudAlert> {
    return this.http.get<FraudAlert>(`${API_BASE_URL}/fraud-alerts/${id}`);
  }

  updateAlertStatus(id: string, status: string): Observable<FraudAlert> {
    return this.http.patch<FraudAlert>(`${API_BASE_URL}/fraud-alerts/${id}/status`, JSON.stringify(status), {
      headers: { 'Content-Type': 'application/json' }
    });
  }

  getTopTriggers(limit = 5): Observable<TopTrigger[]> {
    return this.http.get<TopTrigger[]>(`${API_BASE_URL}/fraud-alerts/top-triggers`, {
      params: new HttpParams().set('limit', limit)
    });
  }

  getDashboardStats(): Observable<DashboardStats> {
    return this.http.get<DashboardStats>(`${API_BASE_URL}/dashboard/stats`);
  }

  getTrends(days = 14): Observable<DailyTrend[]> {
    return this.http.get<DailyTrend[]>(`${API_BASE_URL}/dashboard/trends`, {
      params: new HttpParams().set('days', days)
    });
  }

  getPipelineHealth(): Observable<PipelineHealth> {
    return this.http.get<PipelineHealth>(`${API_BASE_URL}/health/pipeline`);
  }

  getCustomerProfiles(page = 1, pageSize = 25): Observable<PagedResult<CustomerRiskProfile>> {
    const params = buildParams({ page, pageSize });
    return this.http.get<PagedResult<CustomerRiskProfile>>(`${API_BASE_URL}/customer-risk-profiles`, { params });
  }

  getCustomerProfile(accountId: string): Observable<CustomerRiskProfile> {
    return this.http.get<CustomerRiskProfile>(`${API_BASE_URL}/customer-risk-profiles/${accountId}`);
  }

  createTransaction(payload: {
    accountId: string;
    merchantName: string;
    merchantCategory: string;
    amount: number;
    currency: string;
    countryCode: string;
  }): Observable<Transaction> {
    return this.http.post<Transaction>(`${API_BASE_URL}/transactions`, payload);
  }

  login(username: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${API_BASE_URL}/auth/login`, { username, password });
  }
}
