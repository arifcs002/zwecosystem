import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CompanySetting, SettingsService } from '../../../services/settings/settings.service';

const MASK = '********';
const STATUSES = [
  { key: 'processing', label: 'Processing' }, { key: 'packed', label: 'Packed' },
  { key: 'shipped', label: 'Shipped' }, { key: 'completed', label: 'Delivered' }, { key: 'cancelled', label: 'Cancelled' }
];

// One place for: SMS wording per order status, the text printed on invoices, and courier API keys.
@Component({
  selector: 'app-messages-courier',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">✉️ Messages, Invoice &amp; Courier</h1>
      <p class="page-subtitle">What customers are told, what your invoices say, and your courier account</p>
    </div>
    <button class="btn-primary" (click)="save()" [disabled]="saving || loading">{{ saving ? 'Saving…' : 'Save all' }}</button>
  </div>

  <div class="alert-success" *ngIf="successMsg">✅ {{ successMsg }}</div>
  <div class="alert-error" *ngIf="errorMsg">❌ {{ errorMsg }}</div>
  <div class="loading" *ngIf="loading">Loading…</div>

  <ng-container *ngIf="!loading">
    <div class="table-card" style="padding:18px;margin-bottom:16px">
      <h3 style="margin:0 0 4px">SMS to customers</h3>
      <p style="opacity:.65;font-size:13px;margin:0 0 12px">Sent when you move an order to that status. Leave a box empty to use the built-in message. Placeholders: <code>{{ '{order} {shop} {name} {tracking} {total}' }}</code></p>
      <div class="form-group"><label>SMS gateway URL</label>
        <input class="form-input" [(ngModel)]="v['sms_api_url']" placeholder="https://your-sms-provider/send?to={{ '{phone}' }}&amp;msg={{ '{message}' }}" />
        <small style="opacity:.6">Empty = no SMS is sent.</small></div>
      <div class="form-group" *ngFor="let s of statuses">
        <label>When {{ s.label }}</label>
        <input class="form-input" [(ngModel)]="v['sms_template_' + s.key]" [placeholder]="'e.g. Hi {name}, your order {order} is ' + s.label.toLowerCase() + '. — {shop}'" maxlength="300" />
      </div>
    </div>

    <div class="table-card" style="padding:18px;margin-bottom:16px">
      <h3 style="margin:0 0 12px">Invoice</h3>
      <div class="form-group"><label>Header (address, phone, VAT/BIN)</label><textarea class="form-input" rows="2" [(ngModel)]="v['invoice_header_text']" maxlength="400"></textarea></div>
      <div class="form-group"><label>Footer note</label><textarea class="form-input" rows="2" [(ngModel)]="v['invoice_footer_text']" maxlength="400" placeholder="Thank you for shopping with us!"></textarea></div>
    </div>

    <div class="table-card" style="padding:18px">
      <h3 style="margin:0 0 4px">Steadfast Courier</h3>
      <p style="opacity:.65;font-size:13px;margin:0 0 12px">Get these from your Steadfast merchant panel (API settings). They are stored write-only: once saved, they show as ••••••••.</p>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px">
        <div class="form-group"><label>API key</label><input class="form-input" type="password" autocomplete="off" [(ngModel)]="v['secret_steadfast_api_key']" /></div>
        <div class="form-group"><label>Secret key</label><input class="form-input" type="password" autocomplete="off" [(ngModel)]="v['secret_steadfast_secret_key']" /></div>
      </div>
      <small style="opacity:.6">Then open any order and press “Send to Steadfast”.</small>
    </div>
  </ng-container>
</div>`
})
export class MessagesCourierComponent implements OnInit {
  private settings = inject(SettingsService);

  statuses = STATUSES;
  v: Record<string, string> = {};
  loading = true; saving = false;
  successMsg = ''; errorMsg = '';

  private readonly keys = [
    'sms_api_url', ...STATUSES.map(s => 'sms_template_' + s.key),
    'invoice_header_text', 'invoice_footer_text', 'secret_steadfast_api_key', 'secret_steadfast_secret_key'
  ];

  ngOnInit() {
    this.settings.getSettings().subscribe({
      next: (all) => {
        for (const k of this.keys) this.v[k] = all.find(s => s.key === k)?.value ?? '';
        this.loading = false;
      },
      error: () => { this.loading = false; this.errorMsg = 'Could not load settings.'; }
    });
  }

  save() {
    this.saving = true; this.errorMsg = '';
    const payload: CompanySetting[] = this.keys.map(k => ({
      key: k, value: (this.v[k] ?? '').trim(),
      groupName: k.startsWith('secret_') ? 'COURIER' : k.startsWith('invoice_') ? 'INVOICE' : 'NOTIFICATIONS'
    }));
    this.settings.updateSettings(payload).subscribe({
      next: () => { this.saving = false; this.successMsg = 'Saved.'; setTimeout(() => this.successMsg = '', 3000); },
      error: (e) => { this.saving = false; this.errorMsg = e?.status === 403 ? 'Only the shop owner can change courier keys.' : 'Could not save.'; }
    });
  }
}
