import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export type AbandonedStatus = 'OPEN' | 'CONTACTED' | 'RECOVERED' | 'DISMISSED';

export interface AbandonedCartRow {
  id: number;
  customerName?: string | null;
  customerPhone: string;
  shippingAddress?: string | null;
  itemCount: number;
  subtotal: number;
  status: AbandonedStatus;
  lastActivity: string;
  items: { productId: number; name: string; quantity: number; price: number }[];
}

@Injectable({ providedIn: 'root' })
export class AbandonedCartService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/abandoned-carts`;

  // ── Admin ──
  getAll(status?: AbandonedStatus | ''): Observable<AbandonedCartRow[]> {
    return this.http.get<AbandonedCartRow[]>(this.apiUrl, { params: status ? { status } : {} });
  }
  setStatus(id: number, status: AbandonedStatus): Observable<any> {
    return this.http.put(`${this.apiUrl}/${id}/status`, { status });
  }
  remove(id: number): Observable<any> { return this.http.delete(`${this.apiUrl}/${id}`); }

  // ── Storefront (anonymous) ──
  capture(dto: {
    sessionKey: string; customerName?: string; customerPhone: string; shippingAddress?: string;
    items: { productId: number; quantity: number }[];
  }): Observable<{ captured: boolean }> {
    return this.http.post<{ captured: boolean }>(`${this.apiUrl}/capture`, dto);
  }
}
