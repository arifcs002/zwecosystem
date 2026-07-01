import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface PublicOrderItem { productId: number; quantity: number; }

export interface PublicCheckoutRequest {
  items: PublicOrderItem[];
  customerName: string;
  customerPhone: string;
  shippingAddress: string;
  shippingDistrict?: string;
  shippingThana?: string;
  orderNotes?: string;
  paymentMethod: string;
}

export interface PublicOrderResult {
  orderId: number;
  orderNumber: string;
  subtotal: number;
  shippingFee: number;
  total: number;
}

@Injectable({ providedIn: 'root' })
export class OrderService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/orders`;

  // Storefront guest checkout — anonymous; the interceptor attaches X-Tenant-ID
  // from localStorage so the backend knows which store the order belongs to.
  placePublicOrder(dto: PublicCheckoutRequest): Observable<PublicOrderResult> {
    return this.http.post<PublicOrderResult>(`${this.apiUrl}/public`, dto);
  }

  getOrders(): Observable<any[]> {
    return this.http.get<any[]>(this.apiUrl);
  }

  getOrderById(id: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}`);
  }

  updateOrderStatus(id: number, status: string, notes?: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}/status`, { status, notes });
  }

  cancelOrder(id: number): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/${id}/cancel`, {});
  }

  verifyPayment(data: any): Observable<any> {
    return this.http.post<any>(`${environment.apiUrl}/payments/mfs-verify`, data);
  }
}
