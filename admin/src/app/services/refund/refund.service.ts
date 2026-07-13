import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class RefundService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/refunds`;

  getAll(status?: string): Observable<any[]> {
    const url = status && status !== 'ALL' ? `${this.apiUrl}?status=${status}` : this.apiUrl;
    return this.http.get<any[]>(url);
  }

  create(data: { paymentId: number; orderId: number; amount: number; reason?: string }): Observable<any> {
    return this.http.post<any>(this.apiUrl, data);
  }

  approve(id: number): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}/approve`, {});
  }

  reject(id: number, reason?: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}/reject`, { reason });
  }

  complete(id: number): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}/complete`, {});
  }
}
