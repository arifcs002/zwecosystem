import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../services/auth/auth.service';
import { CashierRow, ShiftCurrent, ShiftRow, TeamService } from '../../../services/operations/team.service';

const bdToday = () => new Date(Date.now() + 6 * 3600_000).toISOString().slice(0, 10);

// Each cashier counts the drawer when they start and when they leave; managers see who sold what.
@Component({
  selector: 'app-shifts',
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">🕑 My Shift</h1>
      <p class="page-subtitle">Count the drawer when you start and when you finish</p>
    </div>
  </div>

  <div class="alert-error" *ngIf="errorMsg" role="alert">❌ {{ errorMsg }}</div>
  <div class="alert-success" *ngIf="okMsg" role="status">✅ {{ okMsg }}</div>

  <div class="table-card" style="padding:16px;margin-bottom:16px" *ngIf="cur">
    <ng-container *ngIf="!cur.open">
      <b>No shift is open.</b>
      <div style="display:flex;gap:8px;margin-top:10px;flex-wrap:wrap;align-items:end">
        <label style="font-size:12.5px">Cash in the drawer now (৳)
          <input class="form-input" type="number" min="0" [(ngModel)]="opening" style="margin-top:4px"></label>
        <button class="btn-primary" (click)="open()" [disabled]="busy || opening === null">Start my shift</button>
      </div>
    </ng-container>

    <ng-container *ngIf="cur.open && cur.figures as f">
      <b>Shift open since {{ cur.shift!.openedAt | date:'d MMM, h:mm a' }}</b>
      <table style="width:100%;font-size:14px;margin-top:10px;border-collapse:collapse">
        <tr><td>Cash at the start</td><td style="text-align:right">৳{{ f.openingCash | number:'1.0-0' }}</td></tr>
        <tr><td>+ Your cash sales</td><td style="text-align:right">৳{{ f.cashSales | number:'1.0-0' }}</td></tr>
        <tr><td>+ Cash you took on credit accounts</td><td style="text-align:right">৳{{ f.cashReceivedOnAccount | number:'1.0-0' }}</td></tr>
        <tr><td>− Cash you refunded</td><td style="text-align:right">৳{{ f.cashRefunds | number:'1.0-0' }}</td></tr>
        <tr style="border-top:1px solid rgba(255,255,255,.15);font-weight:800"><td style="padding-top:6px">Should be in the drawer</td><td style="text-align:right;padding-top:6px">৳{{ f.expectedCash | number:'1.0-0' }}</td></tr>
      </table>
      <div style="display:flex;gap:8px;margin-top:12px;flex-wrap:wrap;align-items:end">
        <label style="font-size:12.5px">Cash you counted (৳)
          <input class="form-input" type="number" min="0" [(ngModel)]="counted" style="margin-top:4px"></label>
        <input *ngIf="counted !== null && diff(f.expectedCash) !== 0" class="form-input" style="flex:1;min-width:200px" maxlength="300" [(ngModel)]="note" placeholder="Reason for the difference (required)">
        <button class="btn-primary" (click)="close()" [disabled]="busy || counted === null">Finish my shift</button>
      </div>
      <p *ngIf="counted !== null" style="margin:8px 0 0;font-weight:700" [style.color]="diff(f.expectedCash) === 0 ? '#34d399' : '#fbbf24'">
        {{ diff(f.expectedCash) === 0 ? '✓ The drawer matches.' : (diff(f.expectedCash) < 0 ? 'Short by ৳' + (-diff(f.expectedCash)) : 'Over by ৳' + diff(f.expectedCash)) }}
      </p>
    </ng-container>
  </div>

  <ng-container *ngIf="isManager">
    <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:end;margin-bottom:12px">
      <label style="font-size:12.5px">From <input class="form-input" type="date" [(ngModel)]="from" (change)="loadReport()" style="margin-top:4px"></label>
      <label style="font-size:12.5px">To <input class="form-input" type="date" [(ngModel)]="to" (change)="loadReport()" style="margin-top:4px"></label>
    </div>
    <div class="table-card" style="padding:16px;margin-bottom:16px">
      <h3 style="margin:0 0 8px">Sales by cashier</h3>
      <p *ngIf="!cashiers.length" style="opacity:.65">No sales in this range.</p>
      <table class="data-table" style="width:100%;min-width:0;font-size:13.5px" *ngIf="cashiers.length">
        <thead><tr><th>Cashier</th><th style="text-align:right">Sales</th><th style="text-align:right">Revenue</th><th style="text-align:right">Cash</th><th style="text-align:right">Discounts given</th></tr></thead>
        <tbody><tr *ngFor="let c of cashiers"><td>{{ c.userName || 'Unknown' }}</td><td style="text-align:right">{{ c.orders }}</td><td style="text-align:right">৳{{ c.revenue | number:'1.0-0' }}</td><td style="text-align:right">৳{{ c.cash | number:'1.0-0' }}</td><td style="text-align:right">৳{{ c.discounts | number:'1.0-0' }}</td></tr></tbody>
      </table>
    </div>
    <div class="table-card" style="padding:16px">
      <h3 style="margin:0 0 8px">Shifts</h3>
      <p *ngIf="!shifts.length" style="opacity:.65">No shifts in this range.</p>
      <table class="data-table" style="width:100%;min-width:0;font-size:13px" *ngIf="shifts.length">
        <thead><tr><th>Cashier</th><th>Started</th><th>Finished</th><th style="text-align:right">Expected</th><th style="text-align:right">Counted</th><th style="text-align:right">Difference</th><th>Note</th></tr></thead>
        <tbody><tr *ngFor="let s of shifts">
          <td>{{ s.userName }}</td><td>{{ s.openedAt | date:'d MMM, h:mm a' }}</td><td>{{ s.closedAt ? (s.closedAt | date:'d MMM, h:mm a') : 'still open' }}</td>
          <td style="text-align:right">{{ s.expectedCash === null ? '' : '৳' + (s.expectedCash | number:'1.0-0') }}</td>
          <td style="text-align:right">{{ s.countedCash === null ? '' : '৳' + (s.countedCash | number:'1.0-0') }}</td>
          <td style="text-align:right" [style.color]="gap(s) === 0 ? '' : '#fbbf24'">{{ s.closedAt ? (gap(s) === 0 ? '—' : gap(s)) : '' }}</td><td>{{ s.note }}</td>
        </tr></tbody>
      </table>
    </div>
  </ng-container>
</div>`
})
export class ShiftsComponent implements OnInit {
  private api = inject(TeamService);
  private auth = inject(AuthService);
  isManager = this.auth.hasRight('PAGE_PRODUCTS');
  cur: ShiftCurrent | null = null;
  cashiers: CashierRow[] = []; shifts: ShiftRow[] = [];
  from = bdToday().slice(0, 8) + '01'; to = bdToday();
  opening: number | null = 0; counted: number | null = null; note = '';
  busy = false; errorMsg = ''; okMsg = '';

  ngOnInit() { this.loadCurrent(); if (this.isManager) this.loadReport(); }
  diff(expected: number) { return Math.round(((this.counted ?? 0) - expected) * 100) / 100; }
  gap(s: ShiftRow) { return Math.round(((s.countedCash ?? 0) - (s.expectedCash ?? 0)) * 100) / 100; }

  loadCurrent() { this.api.currentShift().subscribe({ next: c => this.cur = c, error: () => this.errorMsg = 'Could not load your shift.' }); }
  loadReport() {
    this.api.cashiers(this.from, this.to).subscribe({ next: r => this.cashiers = r, error: () => this.cashiers = [] });
    this.api.shiftHistory(this.from, this.to).subscribe({ next: r => this.shifts = r, error: () => this.shifts = [] });
  }

  open() {
    if (this.opening === null) return;
    this.busy = true; this.errorMsg = ''; this.okMsg = '';
    this.api.openShift(this.opening).subscribe({
      next: () => { this.busy = false; this.okMsg = 'Shift started.'; this.loadCurrent(); if (this.isManager) this.loadReport(); },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not start the shift.'; }
    });
  }

  close() {
    if (this.counted === null) return;
    this.busy = true; this.errorMsg = ''; this.okMsg = '';
    this.api.closeShift(this.counted, this.note.trim() || undefined).subscribe({
      next: r => { this.busy = false; this.counted = null; this.note = ''; this.okMsg = r.difference === 0 ? 'Shift finished. The drawer matched.' : `Shift finished. Difference ৳${r.difference}.`; this.loadCurrent(); if (this.isManager) this.loadReport(); },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not finish the shift.'; }
    });
  }
}
