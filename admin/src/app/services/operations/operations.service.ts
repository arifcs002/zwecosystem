import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

// ── Purchasing ──
export interface PurchaseRow { id: number; poNumber: string; status: 'ORDERED' | 'PARTIAL' | 'RECEIVED' | 'CANCELLED'; supplierId: number; supplier: string | null; createdDate: string; orderedValue: number; receivedValue: number; note?: string | null; }
export interface PurchaseItem { id: number; productId: number; name: string; quantity: number; unitCost: number; receivedQty: number; remaining: number; }
export interface PurchaseDetail { id: number; poNumber: string; status: string; supplier: string | null; supplierId: number; note?: string | null; createdDate: string; items: PurchaseItem[]; }
export interface SupplierAccount { id: number; name: string; phoneNumber?: string | null; balance: number; lastActivity: string | null; }
export interface SupplierEntry { id: number; entryType: 'PURCHASE' | 'PAYMENT' | 'ADJUST'; amount: number; method: string | null; note: string | null; createdDate: string; purchaseOrderId: number | null; }
export interface SupplierDetail { supplier: { id: number; name: string; phoneNumber?: string | null }; balance: number; entries: SupplierEntry[]; }

// ── Cash closing ──
export interface CashFigures {
  date: string; openingCash: number; cashSales: number; exchangeCreditApplied: number; cashReceivedOnAccount: number;
  cashRefunds: number; expenses: number; supplierCashPayments: number; expectedCash: number;
  info: { nonCashSales: number; soldOnCredit: number; nonCashReceivedOnAccount: number };
}
export interface CashDay { figures: CashFigures; openingKnown: boolean; closing: { countedCash: number; expectedCash: number; difference: number; note: string | null; closedAt: string } | null; }
export interface CashClosingRow { date: string; openingCash: number; expectedCash: number; countedCash: number; difference: number; note: string | null; closedAt: string; }

// ── Stock count ──
export interface CountRow { id: number; name: string; status: 'OPEN' | 'APPLIED' | 'CANCELLED'; createdDate: string; appliedDate: string | null; lines: number; counted: number; variances: number; }
export interface CountLine { productId: number; name: string; barcode: string | null; sku: string | null; expected: number; counted: number | null; variance: number | null; valueOfVariance: number | null; currentStock: number | null; }
export interface CountDetail { id: number; name: string; status: string; summary: { lines: number; counted: number; notCounted: number; shortages: number; surpluses: number; shortageValue: number; surplusValue: number }; lines: CountLine[]; }

@Injectable({ providedIn: 'root' })
export class OperationsService {
  private http = inject(HttpClient);
  private base = environment.apiUrl;

  // purchasing
  purchases(status?: string): Observable<PurchaseRow[]> { return this.http.get<PurchaseRow[]>(`${this.base}/purchases`, { params: status ? { status } : {} }); }
  purchase(id: number): Observable<PurchaseDetail> { return this.http.get<PurchaseDetail>(`${this.base}/purchases/${id}`); }
  createPurchase(dto: { supplierId: number; items: { productId: number; quantity: number; unitCost: number }[]; note?: string }): Observable<{ id: number; poNumber: string }> { return this.http.post<{ id: number; poNumber: string }>(`${this.base}/purchases`, dto); }
  receivePurchase(id: number, dto: { lines: { itemId: number; quantity: number }[]; paidNow?: number; method?: string; updateCost?: boolean }): Observable<{ status: string; receivedValue: number; owedToSupplier: number }> { return this.http.post<any>(`${this.base}/purchases/${id}/receive`, dto); }
  cancelPurchase(id: number): Observable<any> { return this.http.post(`${this.base}/purchases/${id}/cancel`, {}); }
  supplierAccounts(): Observable<SupplierAccount[]> { return this.http.get<SupplierAccount[]>(`${this.base}/supplier-accounts`); }
  supplierSummary(): Observable<{ totalOwed: number; suppliersOwed: number; advancePaid: number }> { return this.http.get<any>(`${this.base}/supplier-accounts/summary`); }
  supplierDetail(id: number): Observable<SupplierDetail> { return this.http.get<SupplierDetail>(`${this.base}/supplier-accounts/${id}`); }
  paySupplier(supplierId: number, amount: number, method: string, note?: string): Observable<{ balance: number }> { return this.http.post<{ balance: number }>(`${this.base}/supplier-accounts/payments`, { supplierId, amount, method, note }); }

  // cash closing
  cashDay(date: string): Observable<CashDay> { return this.http.get<CashDay>(`${this.base}/cashbook/day`, { params: { date } }); }
  cashClosings(): Observable<CashClosingRow[]> { return this.http.get<CashClosingRow[]>(`${this.base}/cashbook/closings`); }
  closeCash(dto: { date: string; countedCash: number; openingCash?: number; note?: string }): Observable<{ expectedCash: number; countedCash: number; difference: number }> { return this.http.post<any>(`${this.base}/cashbook/close`, dto); }

  // stock count
  counts(): Observable<CountRow[]> { return this.http.get<CountRow[]>(`${this.base}/stockcounts`); }
  count(id: number): Observable<CountDetail> { return this.http.get<CountDetail>(`${this.base}/stockcounts/${id}`); }
  startCount(dto: { name?: string; categoryId?: number | null }): Observable<{ id: number; lines: number }> { return this.http.post<any>(`${this.base}/stockcounts`, dto); }
  setCounted(id: number, lines: { productId: number; countedQty: number | null }[]): Observable<any> { return this.http.put(`${this.base}/stockcounts/${id}/lines`, { lines }); }
  scanCount(id: number, code: string): Observable<{ productId: number; name: string; counted: number; expected: number }> { return this.http.post<any>(`${this.base}/stockcounts/${id}/scan`, { code }); }
  applyCount(id: number): Observable<{ adjusted: number; shortageValue: number; surplusValue: number; notCounted: number }> { return this.http.post<any>(`${this.base}/stockcounts/${id}/apply`, {}); }
  cancelCount(id: number): Observable<any> { return this.http.post(`${this.base}/stockcounts/${id}/cancel`, {}); }
}
