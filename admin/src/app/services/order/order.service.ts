import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class OrderService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/orders`;

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
