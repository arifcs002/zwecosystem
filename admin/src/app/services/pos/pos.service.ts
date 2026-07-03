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
