import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { shareReplay } from 'rxjs/operators';
import { environment } from '../../../environments/environment';
import { CompanySetting } from '../settings/settings.service';
import { Product } from '../product/product.service';
import { Category } from '../category/category.service';
import { Brand } from '../brand/brand.service';

export interface StorefrontData {
  company: { id: number; name: string; logoUrl?: string; subdomain?: string; isActive?: boolean };
  settings: CompanySetting[];
  products: Product[];
  categories: Category[];
  brands: Brand[];
}

@Injectable({ providedIn: 'root' })
export class StorefrontService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/storefront`;

  // Cached per slug (shareReplay) so navigating home ⇄ category ⇄ back — which
  // destroys/recreates the shop component — reuses the response instead of
  // refetching. Cleared on full page reload (fresh JS context) so edits show up.
  private cache = new Map<string, Observable<StorefrontData>>();

  // One anonymous call returns everything the homepage needs — tenant is
  // resolved from the slug server-side, so no dependent round trips.
  getStorefront(slug: string): Observable<StorefrontData> {
    let cached = this.cache.get(slug);
    if (!cached) {
      cached = this.http.get<StorefrontData>(`${this.apiUrl}/${slug}`).pipe(shareReplay(1));
      this.cache.set(slug, cached);
    }
    return cached;
  }

  // Force a fresh fetch next time (e.g. after the shop owner previews edits).
  invalidate(slug?: string) {
    if (slug) this.cache.delete(slug); else this.cache.clear();
  }
}
