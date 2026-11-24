import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface LedgerCustomer { phone: string; name: string | null; balance: number; lastActivity: string; }
export interface LedgerEntry { id: number; entryType: 'SALE' | 'PAYMENT' | 'RETURN' | 'ADJUST'; amount: number; orderId: number | null; method: string | null; note: string | null; createdDate: string; }
export interface LedgerDetail { phone: string; name: string | null; balance: number; creditLimit: number; entries: LedgerEntry[]; }
export interface LedgerSummary { totalDue: number; customersWithDue: number; advanceHeld: number; creditLimit: number; }

@Injectable({ providedIn: 'root' })
export class LedgerService {
  private http = inject(HttpClient);
  private api = `${environment.apiUrl}/ledger`;

  summary(): Observable<LedgerSummary> { return this.http.get<LedgerSummary>(`${this.api}/summary`); }
  customers(search: string, dueOnly: boolean): Observable<LedgerCustomer[]> {
    return this.http.get<LedgerCustomer[]>(`${this.api}/customers`, { params: { search, dueOnly } });
  }
  customer(phone: string): Observable<LedgerDetail> { return this.http.get<LedgerDetail>(`${this.api}/customers/${encodeURIComponent(phone)}`); }
  pay(phone: string, amount: number, method: string, note?: string): Observable<{ balance: number; previousBalance: number }> {
    return this.http.post<{ balance: number; previousBalance: number }>(`${this.api}/payments`, { phone, amount, method, note });
  }
  adjust(phone: string, amount: number, note: string): Observable<{ balance: number }> {
    return this.http.post<{ balance: number }>(`${this.api}/adjustments`, { phone, amount, note });
  }
}
