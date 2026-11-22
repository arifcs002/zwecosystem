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
  private apiUrl = `${environment.apiUrl}/courier/steadfast/orders`;

  book(orderId: number): Observable<CourierBookingResult> { return this.http.post<CourierBookingResult>(`${this.apiUrl}/${orderId}`, {}); }
  refresh(orderId: number): Observable<{ courierStatus: string }> { return this.http.get<{ courierStatus: string }>(`${this.apiUrl}/${orderId}/status`); }
}
