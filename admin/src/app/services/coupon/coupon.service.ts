import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Coupon {
  id: number;
  code: string;
  description?: string | null;
  discountType: 'PERCENT' | 'FIXED';
  discountValue: number;
  minOrderAmount: number;
  maxDiscountAmount?: number | null;
  usageLimit?: number | null;
  usedCount: number;
  startsAt?: string | null;
  expiresAt?: string | null;
  isActive: boolean;
  createdDate: string;
}

export interface CouponUpsert {
  code: string;
  description?: string | null;
  discountType: 'PERCENT' | 'FIXED';
  discountValue: number;
  minOrderAmount: number;
  maxDiscountAmount?: number | null;
  usageLimit?: number | null;
  startsAt?: string | null;
  expiresAt?: string | null;
  isActive: boolean;
}

export interface CouponCheck { valid: boolean; discount: number; message: string; code?: string; }

@Injectable({ providedIn: 'root' })
export class CouponService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/coupons`;

  // ── Admin ──
  getAll(): Observable<Coupon[]> { return this.http.get<Coupon[]>(this.apiUrl); }
  create(dto: CouponUpsert): Observable<any> { return this.http.post(this.apiUrl, dto); }
  update(id: number, dto: CouponUpsert): Observable<any> { return this.http.put(`${this.apiUrl}/${id}`, dto); }
  toggle(id: number): Observable<any> { return this.http.patch(`${this.apiUrl}/${id}/toggle`, {}); }
  remove(id: number): Observable<any> { return this.http.delete(`${this.apiUrl}/${id}`); }

  // ── Storefront (anonymous; the interceptor adds X-Tenant-ID) ──
  validate(code: string, items: { productId: number; quantity: number }[]): Observable<CouponCheck> {
    return this.http.post<CouponCheck>(`${this.apiUrl}/validate`, { code, items });
  }
}
