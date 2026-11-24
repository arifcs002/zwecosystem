import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface LoyaltySettings { enabled: boolean; earnPer100: number; pointValue: number; minRedeem: number; }
export interface LoyaltyLookup { enabled: boolean; phone: string | null; balance: number; pointValue: number; minRedeem: number; worth: number; }
export interface LoyaltyCustomer { phone: string; points: number; lastActivity: string; }
export interface LoyaltyEntry { id: number; entryType: string; points: number; orderId: number | null; note: string | null; createdDate: string; }

@Injectable({ providedIn: 'root' })
export class LoyaltyService {
  private http = inject(HttpClient);
  private api = `${environment.apiUrl}/loyalty`;

  settings(): Observable<LoyaltySettings> { return this.http.get<LoyaltySettings>(`${this.api}/settings`); }
  saveSettings(s: LoyaltySettings): Observable<any> { return this.http.put(`${this.api}/settings`, s); }
  lookup(phone: string): Observable<LoyaltyLookup> { return this.http.get<LoyaltyLookup>(`${this.api}/lookup`, { params: { phone } }); }
  customers(): Observable<LoyaltyCustomer[]> { return this.http.get<LoyaltyCustomer[]>(`${this.api}/customers`); }
  history(phone: string): Observable<{ phone: string; balance: number; entries: LoyaltyEntry[] }> { return this.http.get<any>(`${this.api}/customers/${encodeURIComponent(phone)}`); }
  adjust(phone: string, points: number, note: string): Observable<{ balance: number }> { return this.http.post<{ balance: number }>(`${this.api}/adjustments`, { phone, points, note }); }
}
