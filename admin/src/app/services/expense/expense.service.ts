import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Expense {
  id: number;
  category: string;
  amount: number;
  expenseDate: string; // yyyy-MM-dd
  payee?: string | null;
  note?: string | null;
}

export interface ExpenseUpsert { category: string; amount: number; expenseDate: string; payee?: string | null; note?: string | null; }

export interface ProfitReport {
  from: string; to: string; note: string;
  summary: {
    orders: number; cancelledOrders: number; revenue: number; discounts: number; shippingCollected: number;
    cogs: number; grossProfit: number; grossMarginPercent: number; expenses: number; netProfit: number;
  };
  expensesByCategory: { category: string; total: number }[];
  daily: { date: string; orders: number; revenue: number; cogs: number; expenses: number; profit: number }[];
}

@Injectable({ providedIn: 'root' })
export class ExpenseService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/expenses`;

  getAll(filter: { from?: string; to?: string; category?: string } = {}): Observable<Expense[]> {
    const params: Record<string, string> = {};
    if (filter.from) params['from'] = filter.from;
    if (filter.to) params['to'] = filter.to;
    if (filter.category) params['category'] = filter.category;
    return this.http.get<Expense[]>(this.apiUrl, { params });
  }
  categories(): Observable<string[]> { return this.http.get<string[]>(`${this.apiUrl}/categories`); }
  create(dto: ExpenseUpsert): Observable<any> { return this.http.post(this.apiUrl, dto); }
  update(id: number, dto: ExpenseUpsert): Observable<any> { return this.http.put(`${this.apiUrl}/${id}`, dto); }
  remove(id: number): Observable<any> { return this.http.delete(`${this.apiUrl}/${id}`); }

  profit(from: string, to: string): Observable<ProfitReport> {
    return this.http.get<ProfitReport>(`${environment.apiUrl}/reports/profit`, { params: { from, to } });
  }
}
