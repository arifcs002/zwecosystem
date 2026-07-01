import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface PricingTag {
  id?: number;
  name: string;
  profitPercent: number;
  discountPercent?: number | null;
  promoStartDate?: string | null;
  promoEndDate?: string | null;
  isActive: boolean;
  createdAt?: string;
}

@Injectable({
  providedIn: 'root'
})
export class PricingTagService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/pricingtags`;

  getPricingTags(): Observable<PricingTag[]> {
    return this.http.get<PricingTag[]>(this.apiUrl);
  }

  createPricingTag(tag: PricingTag): Observable<PricingTag> {
    return this.http.post<PricingTag>(this.apiUrl, tag);
  }

  updatePricingTag(id: number, tag: PricingTag): Observable<PricingTag> {
    return this.http.put<PricingTag>(`${this.apiUrl}/${id}`, tag);
  }

  deletePricingTag(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
