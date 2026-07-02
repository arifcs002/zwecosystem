import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface InventoryItem {
  id: number;
  name: string;
  sku: string;
  size?: string;
  stockQuantity: number;
  price: number;
  wholesalePrice: number;
  imageUrl?: string;
  category?: string;
}

export interface InventoryOverview {
  lowStockThreshold: number;
  summary: { skuCount: number; totalUnits: number; stockValue: number; lowCount: number; outCount: number };
  items: InventoryItem[];
}

export interface InventoryMovement {
  id: number;
  product_id: number;
  product_name?: string;
  movement_type: string;
  quantity: number;
  reason?: string;
  unit_cost?: number;
  reference?: string;
  stock_after?: number;
  created_date: string;
}

@Injectable({ providedIn: 'root' })
export class InventoryService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/inventory`;

  getOverview(): Observable<InventoryOverview> {
    return this.http.get<InventoryOverview>(this.apiUrl);
  }

  adjust(productId: number, delta: number, reason?: string): Observable<{ productId: number; stockQuantity: number }> {
    return this.http.post<{ productId: number; stockQuantity: number }>(`${this.apiUrl}/adjust`, { productId, delta, reason });
  }

  purchase(productId: number, quantity: number, unitCost?: number, supplier?: string, reason?: string) {
    return this.http.post<{ productId: number; stockQuantity: number }>(`${this.apiUrl}/purchase`, { productId, quantity, unitCost, supplier, reason });
  }

  getMovements(productId?: number, from?: string, to?: string): Observable<InventoryMovement[]> {
    const params: any = {};
    if (productId) params.productId = productId;
    if (from) params.from = from;
    if (to) params.to = to;
    return this.http.get<InventoryMovement[]>(`${this.apiUrl}/movements`, { params });
  }
}
