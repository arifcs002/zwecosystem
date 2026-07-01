import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../environments/environment';
import {
  StorefrontLayout, STOREFRONT_LAYOUT_KEY, parseLayout
} from '../../models/storefront-layout.model';

export interface CompanySetting {
  companyId?: string;
  key: string;
  value: string;
  groupName: string;
}

@Injectable({
  providedIn: 'root'
})
export class SettingsService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/settings`;

  getSettings(): Observable<CompanySetting[]> {
    return this.http.get<CompanySetting[]>(this.apiUrl);
  }

  // Anonymous-safe whitelist of branding/display settings for the public shop.
  getPublicSettings(): Observable<CompanySetting[]> {
    return this.http.get<CompanySetting[]>(`${this.apiUrl}/public`);
  }

  updateSettings(settings: CompanySetting[]): Observable<any> {
    return this.http.put<any>(this.apiUrl, settings);
  }

  // ── Storefront layout (block builder) ──────────────────────────────────
  // The whole homepage layout lives in one setting value as JSON. These helpers
  // (de)serialise it so callers work with the typed StorefrontLayout, not raw
  // strings. Admin uses getStorefrontLayout(); the public shop uses the
  // whitelisted /public feed and parseLayout() directly.

  getStorefrontLayout(): Observable<StorefrontLayout> {
    return this.getSettings().pipe(
      map(settings => parseLayout(settings.find(s => s.key === STOREFRONT_LAYOUT_KEY)?.value))
    );
  }

  saveStorefrontLayout(layout: StorefrontLayout): Observable<any> {
    return this.updateSettings([
      { key: STOREFRONT_LAYOUT_KEY, value: JSON.stringify(layout), groupName: 'STOREFRONT' }
    ]);
  }
}
