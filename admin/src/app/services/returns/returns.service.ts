import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ReturnableLine { orderItemId: number; productId: number; name: string; sold: number; returned: number; remaining: number; unitPrice: number; refundPerUnit: number; }
export interface ReturnableOrder {
  order: { id: number; orderNumber: string; status: string; total: number; paymentMethod: string; customerName: string | null; customerPhone: string | null };
  items: ReturnableLine[];
}
export interface ReturnRequest {
  orderId: number;
  items: { orderItemId: number; quantity: number; restock: boolean }[];
  refundMethod: 'CASH' | 'ADJUST_DUE';
  reason?: string;
  exchangeItems?: { productId: number; quantity: number }[];
}
export interface ReturnResult { returnNumber: string; kind: string; refundAmount: number; cashToGiveBack: number; customerPays: number; newOrderId: number | null; restocked: number; }

@Injectable({ providedIn: 'root' })
export class ReturnsService {
  private http = inject(HttpClient);
  private api = `${environment.apiUrl}/returns`;

  find(orderNumber: string): Observable<ReturnableOrder> { return this.http.get<ReturnableOrder>(`${this.api}/find`, { params: { orderNumber } }); }
  create(dto: ReturnRequest): Observable<ReturnResult> { return this.http.post<ReturnResult>(this.api, dto); }
}
