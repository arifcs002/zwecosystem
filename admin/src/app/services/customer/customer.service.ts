import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface CustomerSummary {
  phone: string;
  name?: string;
  lastAddress?: string;
  lastDistrict?: string;
  totalOrders: number;
  totalSpent: number;
  lastOrderAt: string;
}

export interface CustomerOrder {
  id: number;
  orderNumber: string;
  status: string;
  total: number;
  paymentMethod: string;
  shippingAddress?: string;
  shippingDistrict?: string;
  shippingThana?: string;
  createdDate: string;
}

export interface CustomerProfile {
  phone: string;
  name?: string;
  totalOrders: number;
  totalSpent: number;
  orders: CustomerOrder[];
}

@Injectable({ providedIn: 'root' })
export class CustomerService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/customers`;

  getCustomers(search?: string): Observable<CustomerSummary[]> {
    const q = search ? `?search=${encodeURIComponent(search)}` : '';
    return this.http.get<CustomerSummary[]>(`${this.apiUrl}${q}`);
  }

  getCustomer(phone: string): Observable<CustomerProfile> {
    return this.http.get<CustomerProfile>(`${this.apiUrl}/${encodeURIComponent(phone)}`);
  }
}
