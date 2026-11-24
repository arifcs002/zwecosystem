import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface PosProduct {
  id: number;
  name: string;
  barcode: string;
  sku: string;
  price: number;
  wholesalePrice: number;
  stockQuantity: number;
}

export interface PosItem { productId: number; quantity: number; price?: number | null; }
export interface PosCheckoutRequest {
  items: PosItem[];
  discount: number;
  paymentMethod: string;
  customerName?: string;
  customerPhone?: string;
  transactionId?: string;
  clientRef?: string;   // unique per sale: a resend returns the original sale instead of a second one
  soldAt?: string;
  amountPaid?: number;   // credit (baki) sales: what the customer pays now
  offline?: boolean;
}

@Injectable({ providedIn: 'root' })
export class PosService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/pos`;

  lookup(barcode: string): Observable<PosProduct> {
    return this.http.get<PosProduct>(`${this.apiUrl}/lookup`, { params: { barcode } });
  }

  checkout(dto: PosCheckoutRequest): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/checkout`, dto);
  }
}
