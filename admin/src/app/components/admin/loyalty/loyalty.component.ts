import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { LoyaltyCustomer, LoyaltyEntry, LoyaltyService, LoyaltySettings } from '../../../services/loyalty/loyalty.service';

// Reward repeat customers: they earn points on what they pay and spend them on later bills at the till.
@Component({
  selector: 'app-loyalty',
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">⭐ Loyalty Points</h1>
      <p class="page-subtitle">Customers earn points on purchases and use them as a discount on their next bill</p>
    </div>
  </div>

  <div class="alert-error" *ngIf="errorMsg" role="alert">❌ {{ errorMsg }}</div>
  <div class="alert-success" *ngIf="okMsg" role="status">✅ {{ okMsg }}</div>

  <div class="table-card" style="padding:16px;margin-bottom:16px" *ngIf="s">
    <label style="display:flex;gap:8px;align-items:center;font-weight:700;margin-bottom:12px">
      <input type="checkbox" [(ngModel)]="s.enabled"> Loyalty points are {{ s.enabled ? 'ON' : 'OFF' }}
    </label>
    <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:10px">
      <label style="font-size:12.5px">Points earned per ৳100 spent
        <input class="form-input" type="number" min="0" max="100" step="0.5" [(ngModel)]="s.earnPer100" style="margin-top:4px"></label>
      <label style="font-size:12.5px">Value of one point (৳)
        <input class="form-input" type="number" min="0" max="100" step="0.1" [(ngModel)]="s.pointValue" style="margin-top:4px"></label>
      <label style="font-size:12.5px">Smallest number of points to use
        <input class="form-input" type="number" min="0" step="1" [(ngModel)]="s.minRedeem" style="margin-top:4px"></label>
    </div>
    <p style="opacity:.7;font-size:13px;margin:10px 0">
      Example: a ৳1,000 purchase earns {{ (1000 / 100 * s.earnPer100) | number:'1.0-0' }} points, worth about
      ৳{{ (1000 / 100 * s.earnPer100 * s.pointValue) | number:'1.0-0' }} on a later bill
      ({{ (s.earnPer100 * s.pointValue) | number:'1.0-1' }}% back). Returned goods take their points back; points used are not refunded on a return.
    </p>
    <button class="btn-primary" (click)="save()" [disabled]="busy">{{ busy ? 'Saving…' : 'Save' }}</button>
  </div>

  <div class="table-card" style="padding:16px" *ngIf="!detail">
    <h3 style="margin:0 0 8px">Customers with points</h3>
    <p *ngIf="!customers.length" style="opacity:.65">No points yet.</p>
    <table class="data-table" style="width:100%;min-width:0;font-size:13.5px" *ngIf="customers.length">
      <thead><tr><th>Phone</th><th style="text-align:right">Points</th><th>Last activity</th><th></th></tr></thead>
      <tbody><tr *ngFor="let c of customers">
        <td>{{ c.phone }}</td><td style="text-align:right;font-weight:700">{{ c.points }}</td><td>{{ c.lastActivity | date:'d MMM yyyy' }}</td>
        <td style="text-align:right"><button class="btn-secondary" (click)="open(c.phone)">Details</button></td>
      </tr></tbody>
    </table>
    <div style="display:flex;gap:8px;margin-top:12px;flex-wrap:wrap">
      <input class="form-input" style="max-width:220px" [(ngModel)]="findPhone" placeholder="Find a phone number">
      <button class="btn-secondary" (click)="open(findPhone)" [disabled]="findPhone.trim().length < 11">Open</button>
    </div>
  </div>

  <div class="table-card" style="padding:16px" *ngIf="detail">
    <button class="btn-secondary" (click)="detail = null; load()">← Back</button>
    <h3 style="margin:12px 0 4px">{{ detail.phone }} · {{ detail.balance }} points</h3>
    <div style="display:flex;gap:8px;flex-wrap:wrap;margin:10px 0">
      <input class="form-input" type="number" style="max-width:130px" [(ngModel)]="adjPoints" placeholder="+50 or -20">
      <input class="form-input" style="flex:1;min-width:200px" maxlength="200" [(ngModel)]="adjNote" placeholder="Reason (required)">
      <button class="btn-primary" (click)="adjust()" [disabled]="busy || !adjPoints || !adjNote.trim()">Add / remove points</button>
    </div>
    <table class="data-table" style="width:100%;min-width:0;font-size:13px">
      <thead><tr><th>When</th><th>What</th><th style="text-align:right">Points</th><th>Note</th></tr></thead>
      <tbody><tr *ngFor="let e of detail.entries">
        <td>{{ e.createdDate | date:'d MMM yyyy, h:mm a' }}</td><td>{{ label(e.entryType) }}</td>
        <td style="text-align:right" [style.color]="e.points < 0 ? '#f87171' : '#34d399'">{{ e.points > 0 ? '+' + e.points : e.points }}</td><td>{{ e.note }}</td>
      </tr></tbody>
    </table>
  </div>
</div>`
})
export class LoyaltyComponent implements OnInit {
  private api = inject(LoyaltyService);
  s: LoyaltySettings | null = null;
  customers: LoyaltyCustomer[] = [];
  detail: { phone: string; balance: number; entries: LoyaltyEntry[] } | null = null;
  findPhone = ''; adjPoints: number | null = null; adjNote = '';
  busy = false; errorMsg = ''; okMsg = '';

  ngOnInit() { this.api.settings().subscribe({ next: s => this.s = s, error: () => this.errorMsg = 'Could not load the settings.' }); this.load(); }
  load() { this.api.customers().subscribe({ next: c => this.customers = c, error: () => { /* leave the list empty */ } }); }

  label(t: string) {
    return ({ EARN: 'Earned on a sale', REDEEM: 'Used on a sale', RETURN_REVERSE: 'Goods returned', CANCEL_EARN: 'Order cancelled', CANCEL_REDEEM: 'Order cancelled (points back)', ADJUST: 'Correction' } as Record<string, string>)[t] || t;
  }

  save() {
    if (!this.s) return;
    this.busy = true; this.errorMsg = ''; this.okMsg = '';
    this.api.saveSettings(this.s).subscribe({
      next: () => { this.busy = false; this.okMsg = 'Saved.'; },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not save.'; }
    });
  }

  open(phone: string) {
    this.errorMsg = ''; this.okMsg = '';
    this.api.history(phone.trim()).subscribe({ next: d => this.detail = d, error: (e: HttpErrorResponse) => this.errorMsg = e?.error?.message || 'Could not open this customer.' });
  }

  adjust() {
    if (!this.detail || !this.adjPoints) return;
    this.busy = true; this.errorMsg = '';
    const phone = this.detail.phone;
    this.api.adjust(phone, this.adjPoints, this.adjNote.trim()).subscribe({
      next: () => { this.busy = false; this.adjPoints = null; this.adjNote = ''; this.api.history(phone).subscribe(d => { this.detail = d; this.okMsg = 'Points updated.'; }); },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not update the points.'; }
    });
  }
}
