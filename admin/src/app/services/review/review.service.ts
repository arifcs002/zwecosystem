import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, tap } from 'rxjs/operators';
import { environment } from '../../../environments/environment';

export type ReviewStatus = 'PENDING' | 'APPROVED' | 'REJECTED';

export interface PublicReview { id: number; reviewerName: string; rating: number; comment?: string | null; isVerified: boolean; date: string; }
export interface ProductReviews { average: number; count: number; reviews: PublicReview[]; }
export interface AdminReview {
  id: number; productId: number; productName: string; reviewerName: string; rating: number;
  comment?: string | null; status: ReviewStatus; isVerified: boolean; date: string;
}

@Injectable({ providedIn: 'root' })
export class ReviewService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/reviews`;

  // Star summary per product key ("<base name>::<categoryId>"), loaded once per page and read by cards.
  private summary: Record<string, { average: number; count: number }> = {};
  private loadedFor = '';

  ensureSummary(tenantId: string | number): void {
    const id = String(tenantId);
    if (this.loadedFor === id) return;
    this.loadedFor = id;
    this.summary = {};
    this.http.get<Record<string, { average: number; count: number }>>(`${this.apiUrl}/public/summary`)
      .pipe(catchError(() => of({}))).subscribe(s => this.summary = s);
  }
  summaryFor(key: string) { return this.summary[key] || null; }

  forProduct(productId: number): Observable<ProductReviews> {
    return this.http.get<ProductReviews>(`${this.apiUrl}/public/product/${productId}`);
  }

  submit(dto: { productId: number; reviewerName: string; rating: number; comment?: string; orderNumber?: string; phone?: string }):
    Observable<{ status: ReviewStatus; verified: boolean; message: string }> {
    return this.http.post<{ status: ReviewStatus; verified: boolean; message: string }>(`${this.apiUrl}/public`, dto)
      .pipe(tap(() => { this.loadedFor = ''; })); // refresh stars next time
  }

  // ── Admin ──
  getAll(status?: ReviewStatus | ''): Observable<AdminReview[]> {
    return this.http.get<AdminReview[]>(this.apiUrl, { params: status ? { status } : {} });
  }
  setStatus(id: number, status: ReviewStatus): Observable<any> { return this.http.put(`${this.apiUrl}/${id}/status`, { status }); }
  remove(id: number): Observable<any> { return this.http.delete(`${this.apiUrl}/${id}`); }
}
