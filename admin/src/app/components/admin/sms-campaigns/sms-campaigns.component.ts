import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { CampaignPreview, CampaignRow, TeamService } from '../../../services/operations/team.service';

// Send an offer or a thank-you SMS to a group of customers, safely: preview first, then confirm the exact number of people.
@Component({
  selector: 'app-sms-campaigns',
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">📣 SMS Campaigns</h1>
      <p class="page-subtitle">Message a group of customers. Uses your own SMS gateway, so it costs your SMS credit.</p>
    </div>
  </div>

  <div class="alert-error" *ngIf="errorMsg" role="alert">❌ {{ errorMsg }}</div>
  <div class="alert-success" *ngIf="okMsg" role="status">✅ {{ okMsg }}</div>

  <div class="table-card" style="padding:16px;margin-bottom:16px">
    <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px">
      <label style="font-size:12.5px">Campaign name
        <input class="form-input" maxlength="80" [(ngModel)]="name" style="margin-top:4px" placeholder="e.g. Eid offer"></label>
      <label style="font-size:12.5px">Who gets it
        <select class="form-input" [(ngModel)]="audience" (ngModelChange)="preview = null" style="margin-top:4px">
          <option value="all">All my customers</option>
          <option value="recent">Bought recently</option>
          <option value="inactive">Haven't bought for a while</option>
          <option value="points">Customers with loyalty points</option>
          <option value="due">Customers who owe me (baki)</option>
        </select></label>
      <label style="font-size:12.5px" *ngIf="audience === 'recent' || audience === 'inactive'">Number of days
        <input class="form-input" type="number" min="1" max="730" [(ngModel)]="days" (ngModelChange)="preview = null" style="margin-top:4px"></label>
    </div>
    <label style="font-size:12.5px;display:block;margin-top:10px">Message ({{ message.length }}/300) — {{ '{' + 'name' + '}' }} becomes the customer's first name
      <textarea class="form-input" rows="3" maxlength="300" [(ngModel)]="message" (ngModelChange)="preview = null" style="margin-top:4px;width:100%" placeholder="Hello {name}, 10% off this week at our shop!"></textarea></label>
    <div style="display:flex;gap:8px;margin-top:10px;flex-wrap:wrap">
      <button class="btn-secondary" (click)="check()" [disabled]="busy || !message.trim()">Preview who will get it</button>
    </div>

    <div *ngIf="preview as p" style="margin-top:12px">
      <p style="margin:0"><b>{{ p.count }} {{ p.count === 1 ? 'person' : 'people' }}</b> will get this
        <span *ngIf="p.optedOut" style="opacity:.7">({{ p.optedOut }} skipped: asked not to get SMS)</span></p>
      <p *ngIf="!p.hasGateway" style="color:#f87171;margin:6px 0">Set up your SMS gateway first (Messages &amp; Courier settings).</p>
      <p *ngIf="p.tooMany" style="color:#f87171;margin:6px 0">One campaign can reach at most {{ p.maxRecipients }} people. Choose a smaller group.</p>
      <div *ngFor="let s of p.sample" style="opacity:.8;font-size:13px;margin-top:4px">✉️ {{ s }}</div>
      <button class="btn-primary" style="margin-top:10px" (click)="send()" [disabled]="busy || !name.trim() || p.count === 0 || p.tooMany || !p.hasGateway">
        {{ busy ? 'Sending…' : 'Send to ' + p.count + ' ' + (p.count === 1 ? 'person' : 'people') }}</button>
    </div>
  </div>

  <div class="table-card" style="padding:16px;margin-bottom:16px">
    <h3 style="margin:0 0 8px">Don't send to</h3>
    <p style="opacity:.65;font-size:13px;margin:0 0 8px">Numbers of customers who asked to stop getting messages. Campaigns always skip them.</p>
    <div style="display:flex;gap:8px;flex-wrap:wrap;margin-bottom:8px">
      <input class="form-input" style="max-width:200px" [(ngModel)]="optPhone" placeholder="01XXXXXXXXX">
      <button class="btn-secondary" (click)="addOpt()" [disabled]="optPhone.trim().length < 11">Add</button>
    </div>
    <span *ngFor="let p of optOuts" style="display:inline-block;border:1px solid rgba(255,255,255,.2);border-radius:14px;padding:2px 10px;margin:0 6px 6px 0;font-size:13px">
      {{ p }} <a href="javascript:void(0)" (click)="removeOpt(p)" aria-label="Remove" style="margin-left:4px">✕</a></span>
  </div>

  <div class="table-card" style="padding:16px" *ngIf="history.length">
    <h3 style="margin:0 0 8px">Past campaigns</h3>
    <table class="data-table" style="width:100%;min-width:0;font-size:13px">
      <thead><tr><th>When</th><th>Name</th><th style="text-align:right">People</th><th style="text-align:right">Sent</th><th style="text-align:right">Failed</th></tr></thead>
      <tbody><tr *ngFor="let c of history"><td>{{ c.createdDate | date:'d MMM yyyy, h:mm a' }}</td><td>{{ c.name }}<div style="opacity:.6;font-size:12px">{{ c.message }}</div></td>
        <td style="text-align:right">{{ c.recipients }}</td><td style="text-align:right">{{ c.sent }}</td><td style="text-align:right">{{ c.failed }}</td></tr></tbody>
    </table>
  </div>
</div>`
})
export class SmsCampaignsComponent implements OnInit {
  private api = inject(TeamService);
  name = ''; audience = 'all'; days = 30; message = '';
  preview: CampaignPreview | null = null;
  history: CampaignRow[] = []; optOuts: string[] = []; optPhone = '';
  busy = false; errorMsg = ''; okMsg = '';

  ngOnInit() { this.loadHistory(); this.loadOpt(); }
  loadHistory() { this.api.campaigns().subscribe({ next: h => this.history = h, error: () => { /* leave empty */ } }); }
  loadOpt() { this.api.optOuts().subscribe({ next: o => this.optOuts = o, error: () => { /* leave empty */ } }); }

  check() {
    this.busy = true; this.errorMsg = ''; this.okMsg = '';
    this.api.preview(this.audience, this.days, this.message).subscribe({
      next: p => { this.busy = false; this.preview = p; },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not prepare the preview.'; }
    });
  }

  send() {
    if (!this.preview || this.busy) return;
    if (!confirm(`Send this SMS to ${this.preview.count} people? This uses your SMS credit and cannot be undone.`)) return;
    this.busy = true; this.errorMsg = ''; this.okMsg = '';
    this.api.sendCampaign({ name: this.name.trim(), audience: this.audience, days: this.days, message: this.message.trim(), expectedCount: this.preview.count }).subscribe({
      next: r => { this.busy = false; this.okMsg = `Done: ${r.sent} sent, ${r.failed} failed.`; this.preview = null; this.message = ''; this.name = ''; this.loadHistory(); },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not send the campaign.'; }
    });
  }

  addOpt() { this.api.addOptOut(this.optPhone.trim()).subscribe({ next: () => { this.optPhone = ''; this.loadOpt(); this.preview = null; }, error: (e: HttpErrorResponse) => this.errorMsg = e?.error?.message || 'Could not add.' }); }
  removeOpt(p: string) { this.api.removeOptOut(p).subscribe({ next: () => { this.loadOpt(); this.preview = null; } }); }
}
