export type TransactionStatus = 'Pending' | 'Approved' | 'Declined' | 'Flagged';
export type AlertSeverity = 'Low' | 'Medium' | 'High' | 'Critical';
export type AlertStatus = 'Open' | 'Reviewed' | 'Dismissed';

export interface Transaction {
  id: string;
  accountId: string;
  merchantName: string;
  merchantCategory: string;
  amount: number;
  currency: string;
  countryCode: string;
  status: TransactionStatus;
  occurredAtUtc: string;
  alertCount: number;
  publishedToKafkaUtc: string | null;
  processedAtUtc: string | null;
  processingError: string | null;
  riskScore: number | null;
  processingSource: string | null;
}

export interface FraudAlert {
  id: string;
  transactionId: string;
  riskScore: number;
  severity: AlertSeverity;
  status: AlertStatus;
  reason: string;
  createdAtUtc: string;
  merchantName: string;
  transactionAmount: number;
  source: string;
  triggeredRules: string | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface DashboardStats {
  totalTransactions: number;
  totalAlerts: number;
  openAlerts: number;
  fraudRate: number;
  totalAmount: number;
  flaggedAmount: number;
  alertsBySeverity: Record<string, number>;
}

export interface PipelineHealth {
  kafkaConnected: boolean;
  pendingCount: number;
  unpublishedCount: number;
  stuckCount: number;
  failedCount: number;
  messagesProduced: number;
  messagesConsumed: number;
  messagesFailed: number;
  lastConsumedAtUtc: string | null;
  avgProcessingLatencyMs: number | null;
}

export interface DailyTrend {
  date: string;
  transactionCount: number;
  flaggedCount: number;
  totalAmount: number;
}

export interface TopTrigger {
  ruleName: string;
  count: number;
}

export interface CustomerRiskProfile {
  accountId: string;
  transactionCount: number;
  avgAmount: number;
  stdDevAmount: number;
  maxAmount: number;
  distinctMerchantCategories: number;
  distinctCountries: number;
  avgTransactionsPerDay: number;
  lastTransactionAtUtc: string | null;
  updatedAtUtc: string;
}

export interface TransactionFilter {
  status?: TransactionStatus;
  accountId?: string;
  search?: string;
  fromUtc?: string;
  toUtc?: string;
}

export interface AlertFilter {
  severity?: AlertSeverity;
  status?: AlertStatus;
  source?: string;
  search?: string;
  transactionId?: string;
}

export interface LoginResponse {
  token: string;
  expiresAtUtc: string;
  username: string;
}
