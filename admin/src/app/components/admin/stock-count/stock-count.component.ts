import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { CountDetail, CountRow, OperationsService } from '../../../services/operations/operations.service';

// Count what is really on the shelves, see the differences, then correct the system in one step.
@Component({
  selector: 'app-stock-count',
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">📋 Stock Count</h1>
      <p class="page-subtitle">Count the shelves, check the differences, then correct the stock</p>
    </div>
    <button class="btn-primary" *ngIf="!detail" (click)="start()" [disabled]="busy">+ Start a new count</button>
    <button class="btn-secondary" *ngIf="detail" (click)="back()">← All counts</button>
  </div>

  <div class="alert-error" *ngIf="errorMsg" role="alert">❌ {{ errorMsg }}</div>
  <div class="alert-success" *ngIf="okMsg" role="status">✅ {{ okMsg }}</div>

  <!-- list -->
  <div class="table-card" style="padding:14px" *ngIf="!detail">
    <p *ngIf="!counts.length" style="opacity:.65">No counts yet. Start one, then scan or type what you find on the shelves. Nothing changes until you press “Apply”.</p>
    <table class="data-table" style="width:100%;min-width:0;font-size:13.5px" *ngIf="counts.length">
      <thead><tr><th>Count</th><th>Status</th><th style="text-align:right">Counted</th><th style="text-align:right">Differences</th><th></th></tr></thead>
      <tbody><tr *ngFor="let c of counts">
        <td>{{ c.name }}<div style="opacity:.6;font-size:12px">{{ c.createdDate | date:'d MMM yyyy' }}</div></td>
        <td>{{ c.status === 'OPEN' ? 'In progress' : c.status === 'APPLIED' ? 'Applied' : 'Cancelled' }}</td>
        <td style="text-align:right">{{ c.counted }} / {{ c.lines }}</td><td style="text-align:right">{{ c.variances }}</td>
        <td style="text-align:right"><button class="btn-secondary" (click)="open(c.id)">{{ c.status === 'OPEN' ? 'Continue' : 'View' }}</button></td>
      </tr></tbody>
    </table>
  </div>

  <!-- one count -->
  <ng-container *ngIf="detail as d">
    <div class="table-card" style="padding:14px;margin-bottom:12px">
      <div style="display:flex;justify-content:space-between;gap:10px;flex-wrap:wrap;align-items:center">
        <div><b>{{ d.name }}</b> <span style="opacity:.6">· {{ d.status === 'OPEN' ? 'in progress' : d.status.toLowerCase() }}</span></div>
        <div style="font-size:13px">Counted {{ d.summary.counted }} of {{ d.summary.lines }} ·
          <span style="color:#f87171">{{ d.summary.shortages }} short (৳{{ d.summary.shortageValue | number:'1.0-0' }})</span> ·
          <span style="color:#34d399">{{ d.summary.surpluses }} over (৳{{ d.summary.surplusValue | number:'1.0-0' }})</span></div>
      </div>

      <form *ngIf="d.status === 'OPEN'" (ngSubmit)="scan()" style="display:flex;gap:8px;margin-top:10px">
        <input class="form-input" style="flex:1" [(ngModel)]="code" name="code" placeholder="Scan or type a barcode — each scan adds 1 to the counted quantity" autocomplete="off">
        <button class="btn-secondary" type="submit" [disabled]="!code.trim()">Add</button>
      </form>
      <p *ngIf="lastScan" role="status" style="margin:8px 0 0;font-size:13px">✓ {{ lastScan }}</p>
    </div>

    <div class="table-card" style="padding:14px;margin-bottom:12px;overflow-x:auto">
      <input class="form-input" style="margin-bottom:8px;max-width:320px" [(ngModel)]="filter" placeholder="Filter by name or barcode">
      <label style="font-size:13px;margin-left:12px"><input type="checkbox" [(ngModel)]="onlyDiff"> only differences</label>
      <table class="data-table" style="width:100%;min-width:0;font-size:13px">
        <thead><tr><th>Product</th><th style="text-align:right">System says</th><th style="text-align:right">Counted</th><th style="text-align:right">Difference</th></tr></thead>
        <tbody>
          <tr *ngFor="let l of visible(d)">
            <td>{{ l.name }}<div style="opacity:.55;font-size:11.5px">{{ l.barcode }}</div></td>
            <td style="text-align:right">{{ l.expected }}<span *ngIf="l.currentStock !== null && l.currentStock !== l.expected" style="opacity:.6;font-size:11px" title="Stock changed (sales) since the count started"> (now {{ l.currentStock }})</span></td>
            <td style="text-align:right">
              <input *ngIf="d.status === 'OPEN'" class="form-input" type="number" min="0" style="width:90px;text-align:right" [ngModel]="l.counted" (change)="edit(l, $any($event.target).value)">
              <span *ngIf="d.status !== 'OPEN'">{{ l.counted ?? '—' }}</span>
            </td>
            <td style="text-align:right;font-weight:700" [style.color]="!l.variance ? '' : l.variance < 0 ? '#f87171' : '#34d399'">{{ l.variance === null ? '' : l.variance > 0 ? '+' + l.variance : l.variance }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div style="display:flex;gap:10px;flex-wrap:wrap" *ngIf="d.status === 'OPEN'">
      <button class="btn-primary" (click)="apply()" [disabled]="busy || d.summary.counted === 0">Apply {{ d.summary.counted }} counted products to the stock</button>
      <button class="btn-secondary" (click)="cancel()" [disabled]="busy">Cancel this count</button>
    </div>
    <p *ngIf="d.status === 'OPEN'" style="opacity:.65;font-size:12.5px;margin-top:8px">
      Only counted products are corrected (each by counted − system). Products you didn't count stay as they are. Sales made while you count are kept.
    </p>
  </ng-container>
</div>`
})
export class StockCountComponent implements OnInit {
  private api = inject(OperationsService);
  counts: CountRow[] = []; detail: CountDetail | null = null;
  code = ''; lastScan = ''; filter = ''; onlyDiff = false;
  busy = false; errorMsg = ''; okMsg = '';

  ngOnInit() { this.loadList(); }
  loadList() { this.api.counts().subscribe({ next: c => this.counts = c, error: () => this.errorMsg = 'Could not load your counts.' }); }

  start() {
    this.busy = true; this.errorMsg = '';
    this.api.startCount({}).subscribe({
      next: r => { this.busy = false; this.open(r.id); },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not start a count.'; }
    });
  }
  open(id: number) { this.errorMsg = ''; this.okMsg = ''; this.lastScan = ''; this.api.count(id).subscribe({ next: d => this.detail = d, error: () => this.errorMsg = 'Could not open this count.' }); }
  back() { this.detail = null; this.loadList(); }

  visible(d: CountDetail) {
    const f = this.filter.trim().toLowerCase();
    return d.lines.filter(l => (!this.onlyDiff || (l.variance ?? 0) !== 0) && (!f || l.name.toLowerCase().includes(f) || (l.barcode || '').toLowerCase().includes(f)));
  }

  edit(l: { productId: number; counted: number | null }, raw: string) {
    if (!this.detail) return;
    const qty = raw.trim() === '' ? null : Math.floor(Number(raw));
    if (qty !== null && (!isFinite(qty) || qty < 0)) { this.errorMsg = 'Enter a quantity of 0 or more.'; return; }
    this.errorMsg = '';
    this.api.setCounted(this.detail.id, [{ productId: l.productId, countedQty: qty }]).subscribe({
      next: () => this.open(this.detail!.id),
      error: (e: HttpErrorResponse) => this.errorMsg = e?.error?.message || 'Could not save.'
    });
  }

  scan() {
    if (!this.detail || !this.code.trim()) return;
    const code = this.code.trim(); this.code = ''; this.errorMsg = '';
    this.api.scanCount(this.detail.id, code).subscribe({
      next: r => { this.lastScan = `${r.name}: counted ${r.counted} (system says ${r.expected})`; this.open(this.detail!.id); },
      error: (e: HttpErrorResponse) => { this.lastScan = ''; this.errorMsg = e?.error?.message || 'Could not count that.'; }
    });
  }

  apply() {
    if (!this.detail || this.busy) return;
    if (!confirm(`Correct the stock of ${this.detail.summary.counted} counted products? This cannot be undone.`)) return;
    this.busy = true; this.errorMsg = '';
    this.api.applyCount(this.detail.id).subscribe({
      next: r => {
        this.busy = false;
        const msg = `Done: ${r.adjusted} products corrected (short ৳${Math.round(r.shortageValue)}, over ৳${Math.round(r.surplusValue)}).`;
        // reload the count WITHOUT clearing the message (open() resets it)
        this.api.count(this.detail!.id).subscribe({ next: d => { this.detail = d; this.okMsg = msg; }, error: () => { this.okMsg = msg; } });
      },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not apply the count.'; }
    });
  }

  cancel() {
    if (!this.detail || !confirm('Cancel this count? Nothing will be changed in your stock.')) return;
    this.api.cancelCount(this.detail.id).subscribe({ next: () => { this.okMsg = 'Count cancelled.'; this.back(); }, error: (e: HttpErrorResponse) => this.errorMsg = e?.error?.message || 'Could not cancel.' });
  }
}
