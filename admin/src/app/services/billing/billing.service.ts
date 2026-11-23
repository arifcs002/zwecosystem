import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface BillingPayment {
  id: number; amount: number; method: string; reference?: string | null; months: number;
  periodStart: string; periodEnd: string; createdDate: string;
}

export interface BillingSummary {
  plan: { id: number; name: string; price: number; billingCycle: string } | null;
  expiresAt: string | null;
  status: 'active' | 'grace' | 'expired';
  daysLeft: number | null;
  graceDays: number;
  modules: { pos: boolean; ecommerce: boolean; multiStaff: boolean };
  limits: { maxProducts: number | null };
  payments: BillingPayment[];
}

export interface PaymentInput { planId?: number | null; amount: number; method: string; reference?: string; months: number; note?: string; }

@Injectable({ providedIn: 'root' })
export class BillingService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/billing`;

  mine(): Observable<BillingSummary> { return this.http.get<BillingSummary>(`${this.apiUrl}/my`); }
  forCompany(id: number): Observable<BillingSummary> { return this.http.get<BillingSummary>(`${this.apiUrl}/companies/${id}`); }
  recordPayment(id: number, dto: PaymentInput): Observable<BillingSummary> {
    return this.http.post<BillingSummary>(`${this.apiUrl}/companies/${id}/payments`, dto);
  }
}
