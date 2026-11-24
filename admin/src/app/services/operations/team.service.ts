import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ShiftFigures { openingCash: number; cashSales: number; cashReceivedOnAccount: number; cashRefunds: number; expectedCash: number; }
export interface ShiftCurrent { open: boolean; shift?: { id: number; openedAt: string; openingCash: number }; figures?: ShiftFigures; }
export interface ShiftRow { id: number; userId: number; userName: string | null; openedAt: string; openingCash: number; closedAt: string | null; expectedCash: number | null; countedCash: number | null; note: string | null; }
export interface CashierRow { userId: number | null; userName: string | null; orders: number; revenue: number; discounts: number; cash: number; }

export interface CampaignPreview { count: number; optedOut: number; tooMany: boolean; maxRecipients: number; hasGateway: boolean; sample: string[]; }
export interface CampaignRow { id: number; name: string; audience: string; message: string; recipients: number; sent: number; failed: number; createdDate: string; }

// Cashier shifts and customer SMS campaigns.
@Injectable({ providedIn: 'root' })
export class TeamService {
  private http = inject(HttpClient);
  private base = environment.apiUrl;

  currentShift(): Observable<ShiftCurrent> { return this.http.get<ShiftCurrent>(`${this.base}/shifts/current`); }
  openShift(openingCash: number): Observable<any> { return this.http.post(`${this.base}/shifts/open`, { openingCash }); }
  closeShift(countedCash: number, note?: string): Observable<{ expectedCash: number; countedCash: number; difference: number }> { return this.http.post<any>(`${this.base}/shifts/close`, { countedCash, note }); }
  shiftHistory(from: string, to: string): Observable<ShiftRow[]> { return this.http.get<ShiftRow[]>(`${this.base}/shifts/history`, { params: { from, to } }); }
  cashiers(from: string, to: string): Observable<CashierRow[]> { return this.http.get<CashierRow[]>(`${this.base}/shifts/cashiers`, { params: { from, to } }); }

  preview(audience: string, days: number, message: string): Observable<CampaignPreview> { return this.http.post<CampaignPreview>(`${this.base}/sms-campaigns/preview`, { audience, days, message }); }
  sendCampaign(dto: { name: string; audience: string; days: number; message: string; expectedCount: number }): Observable<{ recipients: number; sent: number; failed: number }> { return this.http.post<any>(`${this.base}/sms-campaigns/send`, dto); }
  campaigns(): Observable<CampaignRow[]> { return this.http.get<CampaignRow[]>(`${this.base}/sms-campaigns`); }
  optOuts(): Observable<string[]> { return this.http.get<string[]>(`${this.base}/sms-campaigns/optouts`); }
  addOptOut(phone: string): Observable<any> { return this.http.post(`${this.base}/sms-campaigns/optouts`, { phone }); }
  removeOptOut(phone: string): Observable<any> { return this.http.delete(`${this.base}/sms-campaigns/optouts/${encodeURIComponent(phone)}`); }
}
