import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface CourierBookingResult {
  courierName: string; trackingNumber: string; courierConsignmentId: string; courierStatus: string;
}

@Injectable({ providedIn: 'root' })
export class CourierService {
  private http = inject(HttpClient);
  private url(courier: 'steadfast' | 'pathao') { return `${environment.apiUrl}/courier/${courier}/orders`; }

  book(orderId: number, courier: 'steadfast' | 'pathao' = 'steadfast'): Observable<CourierBookingResult> { return this.http.post<CourierBookingResult>(`${this.url(courier)}/${orderId}`, {}); }
  refresh(orderId: number, courier: 'steadfast' | 'pathao' = 'steadfast'): Observable<{ courierStatus: string }> { return this.http.get<{ courierStatus: string }>(`${this.url(courier)}/${orderId}/status`); }
}
