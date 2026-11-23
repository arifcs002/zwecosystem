import { Injectable, NgZone, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { BehaviorSubject, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';
import { IdbStoreSpec, idbDelete, idbGetAll, idbPut, idbReplaceAll, openDb } from '../../utils/idb.util';
import { PosCheckoutRequest, PosProduct } from './pos.service';

export interface CatalogItem { id: number; name: string; barcode: string; sku: string; price: number; stockQuantity: number; uniqueCode?: string | null; }

export interface OfflineSaleLine { productId: number; name: string; barcode: string; quantity: number; unitPrice: number; }

export interface OfflineSale {
  clientRef: string;            // also the idempotency key the server uses to ignore retries
  localNumber: string;          // what the cashier sees on the receipt until the server gives the real one
  soldAt: string;               // ISO time the sale really happened
  request: PosCheckoutRequest;  // items, discount, payment method, customer
  lines: OfflineSaleLine[];     // for the local receipt
  subtotal: number; discount: number; total: number;
  status: 'pending' | 'failed';
  error?: string;
  attempts: number;
}

const STORES: IdbStoreSpec[] = [
  { name: 'catalog', keyPath: 'id' },
  { name: 'sales', keyPath: 'clientRef' }
];

/**
 * Keeps the till working without a connection:
 *  • a local copy of the product list (no cost prices) so scanning still finds items;
 *  • a queue of sales made offline, sent to the server later.
 * Safety comes from the server: every queued sale carries a unique reference, so a retry after a dropped
 * connection returns the original sale instead of charging twice (see POSController.POSCheckout).
 * Data is stored per shop on the device.
 */
@Injectable({ providedIn: 'root' })
export class OfflinePosService {
  private http = inject(HttpClient);
  private auth = inject(AuthService);
  private zone = inject(NgZone);
  private api = `${environment.apiUrl}/pos`;

  private db: IDBDatabase | null = null;
  private dbFor: string | null = null;
  private catalogIndex: Map<string, CatalogItem> | null = null;
  private syncing = false;
  private started = false;

  /** Sales waiting to be sent, and ones the server refused (need a person to look at them). */
  readonly pending$ = new BehaviorSubject<number>(0);
  readonly failed$ = new BehaviorSubject<OfflineSale[]>([]);
  readonly online$ = new BehaviorSubject<boolean>(typeof navigator === 'undefined' ? true : navigator.onLine);
  readonly syncing$ = new BehaviorSubject<boolean>(false);
  readonly catalogCount$ = new BehaviorSubject<number>(0);
  readonly storageOk$ = new BehaviorSubject<boolean>(true);

  /** Call once from the POS screen. Safe to call again. */
  start() {
    if (this.started) return;
    this.started = true;
    window.addEventListener('online', () => this.zone.run(() => { this.online$.next(true); void this.syncAll(); }));
    window.addEventListener('offline', () => this.zone.run(() => this.online$.next(false)));
    this.zone.runOutsideAngular(() => setInterval(() => { if (navigator.onLine) this.zone.run(() => void this.syncAll()); }, 30000));
    void this.syncAll();
    void this.refreshCatalog();
  }

  private async open(): Promise<IDBDatabase | null> {
    const company = this.auth.currentUserValue?.companyId ?? localStorage.getItem('tenant_company_id') ?? 'x';
    const name = `auleco-pos-${company}`;
    if (this.db && this.dbFor === name) return this.db;
    try {
      this.db = await openDb(name, STORES);
      this.dbFor = name;
      this.catalogIndex = null;
      this.storageOk$.next(true);
      return this.db;
    } catch {
      this.storageOk$.next(false);
      return null;
    }
  }

  // ── product copy ─────────────────────────────────────────────────────────

  async refreshCatalog(): Promise<void> {
    if (!navigator.onLine) return;
    const db = await this.open();
    if (!db) return;
    try {
      const res = await firstValueFrom(this.http.get<{ items: CatalogItem[] }>(`${this.api}/catalog`, { headers: this.quiet() }));
      await idbReplaceAll(db, 'catalog', res.items);
      this.catalogIndex = null;
      this.catalogCount$.next(res.items.length);
    } catch { /* keep the previous copy */ }
  }

  private async index(): Promise<Map<string, CatalogItem>> {
    if (this.catalogIndex) return this.catalogIndex;
    const map = new Map<string, CatalogItem>();
    const db = await this.open();
    if (db) {
      const rows = await idbGetAll<CatalogItem>(db, 'catalog').catch(() => [] as CatalogItem[]);
      for (const r of rows) {
        for (const k of [r.barcode, r.sku, r.uniqueCode, String(r.id)]) if (k) map.set(String(k).trim().toLowerCase(), r);
      }
      this.catalogCount$.next(rows.length);
    }
    this.catalogIndex = map;
    return map;
  }

  /** Finds a product in the local copy by barcode, SKU, product code or id. */
  async findLocal(code: string): Promise<PosProduct | null> {
    const hit = (await this.index()).get(code.trim().toLowerCase());
    return hit ? { id: hit.id, name: hit.name, barcode: hit.barcode, sku: hit.sku, price: hit.price, wholesalePrice: 0, stockQuantity: hit.stockQuantity } : null;
  }

  /** Keeps the local stock honest while offline so the same last item can't be sold twice at the counter. */
  private async adjustLocalStock(lines: OfflineSaleLine[]) {
    const db = await this.open();
    if (!db) return;
    const idx = await this.index();
    const seen = new Set<number>();
    for (const l of lines) {
      const item = [...idx.values()].find(i => i.id === l.productId);
      if (!item || seen.has(item.id)) continue;
      seen.add(item.id);
      item.stockQuantity -= lines.filter(x => x.productId === item.id).reduce((s, x) => s + x.quantity, 0);
      await idbPut(db, 'catalog', item).catch(() => { /* not critical */ });
    }
  }

  // ── sale queue ───────────────────────────────────────────────────────────

  static newRef(): string {
    const c: any = (globalThis as any).crypto;
    if (c?.randomUUID) return c.randomUUID();
    return 'r' + Date.now().toString(36) + Math.random().toString(36).slice(2, 12);
  }

  async queueSale(sale: Omit<OfflineSale, 'status' | 'attempts' | 'localNumber'>): Promise<OfflineSale | null> {
    const db = await this.open();
    if (!db) return null;
    const full: OfflineSale = { ...sale, status: 'pending', attempts: 0, localNumber: 'OFFLINE-' + sale.clientRef.slice(0, 8).toUpperCase() };
    try { await idbPut(db, 'sales', full); } catch { return null; }
    await this.adjustLocalStock(sale.lines);
    await this.recount();
    return full;
  }

  private async recount() {
    const db = await this.open();
    if (!db) return;
    const all = await idbGetAll<OfflineSale>(db, 'sales').catch(() => [] as OfflineSale[]);
    this.pending$.next(all.filter(s => s.status === 'pending').length);
    this.failed$.next(all.filter(s => s.status === 'failed'));
  }

  /** Sends queued sales one by one, oldest first. Stops at the first connection problem. */
  async syncAll(): Promise<void> {
    if (this.syncing || !navigator.onLine) { await this.recount(); return; }
    const db = await this.open();
    if (!db) return;
    this.syncing = true; this.syncing$.next(true);
    try {
      const all = (await idbGetAll<OfflineSale>(db, 'sales')).filter(s => s.status === 'pending').sort((a, b) => a.soldAt.localeCompare(b.soldAt));
      for (const sale of all) {
        try {
          await firstValueFrom(this.http.post(`${this.api}/checkout`, { ...sale.request, clientRef: sale.clientRef, soldAt: sale.soldAt, offline: true }, { headers: this.quiet() }));
          await idbDelete(db, 'sales', sale.clientRef);                      // sent (or already recorded): done
        } catch (e) {
          const err = e as HttpErrorResponse;
          if (err.status === 400 || err.status === 402 || err.status === 403 || err.status === 409 || err.status === 404) {
            // The server understood and refused (e.g. product deleted, plan expired). Retrying can't help: keep it for a person.
            sale.status = 'failed'; sale.attempts++;
            sale.error = err.error?.message || 'The server did not accept this sale.';
            await idbPut(db, 'sales', sale);
          } else {
            break;                                                           // no connection / server down / session expired: try later
          }
        }
      }
    } finally {
      this.syncing = false; this.syncing$.next(false);
      await this.recount();
      void this.refreshCatalog();
    }
  }

  async retry(clientRef: string) {
    const db = await this.open(); if (!db) return;
    const all = await idbGetAll<OfflineSale>(db, 'sales');
    const s = all.find(x => x.clientRef === clientRef);
    if (s) { s.status = 'pending'; s.error = undefined; await idbPut(db, 'sales', s); }
    await this.recount();
    void this.syncAll();
  }

  async discard(clientRef: string) {
    const db = await this.open(); if (!db) return;
    await idbDelete(db, 'sales', clientRef).catch(() => { /* ignore */ });
    await this.recount();
  }

  /** Asks the interceptor not to pop an error toast: sync failures are shown in the POS status bar instead. */
  private quiet() { return new HttpHeaders({ 'X-Quiet': '1' }); }
}
