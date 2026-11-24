import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LedgerCustomer, LedgerDetail, LedgerService, LedgerSummary } from '../../../services/ledger/ledger.service';
import { AuthService } from '../../../services/auth/auth.service';

// "Baki" — who owes the shop how much. Money out (credit sales) and in (payments) are listed per customer;
// nothing is ever edited or deleted, a correction is a new line with a reason.
@Component({
  selector: 'app-customer-ledger',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">📒 Customer Accounts (Baki)</h1>
      <p class="page-subtitle">Who owes you money, and every payment received</p>
    </div>
  </div>

  <div class="stat-row" *ngIf="summary" style="display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:12px;margin-bottom:16px">
    <div class="table-card" style="padding:14px"><div style="opacity:.6;font-size:12px">Total owed to you</div><div style="font-size:24px;font-weight:800;color:#fbbf24">৳{{ summary.totalDue | number:'1.0-0' }}</div></div>
    <div class="table-card" style="padding:14px"><div style="opacity:.6;font-size:12px">Customers who owe</div><div style="font-size:24px;font-weight:800">{{ summary.customersWithDue }}</div></div>
    <div class="table-card" style="padding:14px" *ngIf="summary.advanceHeld > 0"><div style="opacity:.6;font-size:12px">Paid in advance</div><div style="font-size:24px;font-weight:800;color:#34d399">৳{{ summary.advanceHeld | number:'1.0-0' }}</div></div>
    <div class="table-card" style="padding:14px"><div style="opacity:.6;font-size:12px">Credit limit per customer</div><div style="font-size:24px;font-weight:800">{{ summary.creditLimit ? ('৳' + (summary.creditLimit | number:'1.0-0')) : 'No limit' }}</div></div>
  </div>

  <div class="alert-error" *ngIf="errorMsg" role="alert">❌ {{ errorMsg }}</div>
  <div class="alert-success" *ngIf="okMsg" role="status">✅ {{ okMsg }}</div>

  <div style="display:grid;grid-template-columns:minmax(0,1.1fr) minmax(0,1fr);gap:16px" class="ledger-grid">
    <div class="table-card" style="padding:14px;min-width:0;overflow-x:auto">
      <div style="display:flex;gap:10px;flex-wrap:wrap;margin-bottom:10px">
        <input class="form-input" style="flex:1;min-width:180px" placeholder="Search name or phone" [(ngModel)]="search" (ngModelChange)="load()">
        <label style="display:flex;align-items:center;gap:6px;font-size:13px"><input type="checkbox" [(ngModel)]="dueOnly" (ngModelChange)="load()"> Only who owe</label>
      </div>
      <div *ngIf="loading" style="opacity:.6;padding:12px">Loading…</div>
      <div *ngIf="!loading && customers.length === 0" style="opacity:.6;padding:12px">No customer accounts yet. Choose “Credit (baki)” when you sell at the POS.</div>
      <table class="data-table" style="width:100%;min-width:0;font-size:13.5px" *ngIf="customers.length">
        <thead><tr><th>Customer</th><th style="text-align:right">Owes</th></tr></thead>
        <tbody>
          <tr *ngFor="let c of customers" (click)="open(c.phone)" style="cursor:pointer" [style.background]="detail?.phone === c.phone ? 'rgba(139,92,246,.12)' : ''">
            <td>{{ c.name || '—' }}<div style="opacity:.6;font-size:12px">{{ c.phone }}</div></td>
            <td style="text-align:right;font-weight:700" [style.color]="c.balance > 0 ? '#fbbf24' : c.balance < 0 ? '#34d399' : ''">
              {{ c.balance < 0 ? '(advance) ' : '' }}৳{{ (c.balance < 0 ? -c.balance : c.balance) | number:'1.0-0' }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="table-card" style="padding:14px" *ngIf="detail as d; else pick">
      <h3 style="margin:0">{{ d.name || d.phone }}</h3>
      <div style="opacity:.65;font-size:13px">{{ d.phone }}</div>
      <div style="font-size:28px;font-weight:800;margin:8px 0" [style.color]="d.balance > 0 ? '#fbbf24' : '#34d399'">
        {{ d.balance > 0 ? 'Owes' : d.balance < 0 ? 'Advance' : 'Clear' }} ৳{{ (d.balance < 0 ? -d.balance : d.balance) | number:'1.0-0' }}
      </div>

      <form (ngSubmit)="receive()" style="display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-bottom:6px">
        <input class="form-input" type="number" min="1" [(ngModel)]="payAmount" name="amt" placeholder="Amount received (৳)">
        <select class="form-input" [(ngModel)]="payMethod" name="mth">
          <option value="cash">Cash</option><option value="bkash">bKash</option><option value="nagad">Nagad</option><option value="bank">Bank</option><option value="other">Other</option>
        </select>
        <input class="form-input" style="grid-column:1/-1" [(ngModel)]="payNote" name="note" maxlength="300" placeholder="Note / TrxID (optional)">
        <button class="btn-primary" style="grid-column:1/-1" type="submit" [disabled]="busy || !payAmount || payAmount <= 0">{{ busy ? 'Saving…' : 'Receive payment' }}</button>
      </form>

      <details *ngIf="isOwner" style="margin:8px 0">
        <summary style="cursor:pointer;font-size:13px;opacity:.8">Correct this account (owner only)</summary>
        <form (ngSubmit)="adjust()" style="display:grid;grid-template-columns:1fr;gap:8px;margin-top:8px">
          <input class="form-input" type="number" [(ngModel)]="adjAmount" name="aamt" placeholder="Amount: negative = owes less, positive = owes more">
          <input class="form-input" [(ngModel)]="adjNote" name="anote" maxlength="300" placeholder="Reason (required)">
          <button class="btn-secondary" type="submit" [disabled]="busy || !adjAmount || !adjNote.trim()">Save correction</button>
        </form>
      </details>

      <div style="font-size:13px;font-weight:700;margin:12px 0 6px">History</div>
      <div *ngFor="let e of d.entries" style="display:flex;justify-content:space-between;gap:10px;padding:6px 0;border-top:1px solid rgba(255,255,255,.07);font-size:13px">
        <span>
          <b>{{ label(e.entryType) }}</b>
          <span style="opacity:.6"> · {{ e.createdDate | date:'d MMM yy, h:mm a' }}<span *ngIf="e.method"> · {{ e.method }}</span></span>
          <div *ngIf="e.note" style="opacity:.6;font-size:12px">{{ e.note }}</div>
        </span>
        <span [style.color]="e.amount > 0 ? '#fbbf24' : '#34d399'" style="white-space:nowrap">{{ e.amount > 0 ? '+' : '−' }}৳{{ (e.amount < 0 ? -e.amount : e.amount) | number:'1.0-0' }}</span>
      </div>
    </div>
    <ng-template #pick><div class="table-card" style="padding:24px;opacity:.6">Choose a customer to see their account.</div></ng-template>
  </div>
</div>`,
  styles: [`@media (max-width: 860px) { .ledger-grid { grid-template-columns: 1fr !important; } }`]
})
export class CustomerLedgerComponent implements OnInit {
  private ledger = inject(LedgerService);
  private auth = inject(AuthService);

  summary: LedgerSummary | null = null;
  customers: LedgerCustomer[] = [];
  detail: LedgerDetail | null = null;
  search = ''; dueOnly = false;
  loading = false; busy = false;
  errorMsg = ''; okMsg = '';
  payAmount: number | null = null; payMethod = 'cash'; payNote = '';
  adjAmount: number | null = null; adjNote = '';

  get isOwner() { return this.auth.isCompanyAdmin() || this.auth.isSuperAdmin(); }

  ngOnInit() { this.load(); this.loadSummary(); }

  label(t: string) { return ({ SALE: 'Credit sale', PAYMENT: 'Payment received', RETURN: 'Returned / cancelled', ADJUST: 'Correction' } as Record<string, string>)[t] || t; }

  private loadSummary() { this.ledger.summary().subscribe({ next: s => this.summary = s, error: () => { /* cards just stay hidden */ } }); }

  load() {
    this.loading = true;
    this.ledger.customers(this.search.trim(), this.dueOnly).subscribe({
      next: c => { this.customers = c; this.loading = false; },
      error: () => { this.loading = false; this.errorMsg = 'Could not load customer accounts.'; }
    });
  }

  open(phone: string) {
    this.errorMsg = ''; this.okMsg = '';
    this.ledger.customer(phone).subscribe({ next: d => this.detail = d, error: () => this.errorMsg = 'Could not open this account.' });
  }

  receive() {
    if (!this.detail || !this.payAmount || this.busy) return;
    this.busy = true; this.errorMsg = ''; this.okMsg = '';
    this.ledger.pay(this.detail.phone, this.payAmount, this.payMethod, this.payNote || undefined).subscribe({
      next: r => {
        this.busy = false; this.payAmount = null; this.payNote = '';
        this.okMsg = `Payment saved. New balance ৳${Math.round(r.balance)}.`;
        this.open(this.detail!.phone); this.load(); this.loadSummary();
      },
      error: e => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not save the payment.'; }
    });
  }

  adjust() {
    if (!this.detail || !this.adjAmount || !this.adjNote.trim() || this.busy) return;
    this.busy = true; this.errorMsg = ''; this.okMsg = '';
    this.ledger.adjust(this.detail.phone, this.adjAmount, this.adjNote.trim()).subscribe({
      next: () => { this.busy = false; this.adjAmount = null; this.adjNote = ''; this.okMsg = 'Correction saved.'; this.open(this.detail!.phone); this.load(); this.loadSummary(); },
      error: e => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not save the correction.'; }
    });
  }
}
