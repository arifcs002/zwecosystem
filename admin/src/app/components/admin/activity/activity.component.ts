import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivityRow, CustomerAdminService, StaffSession } from '../../../services/customer-admin/customer-admin.service';
import { ConfirmDialogService } from '../../../services/confirm-dialog/confirm-dialog.service';

const AREAS = [
  { value: '', label: 'Everything' }, { value: 'order', label: 'Orders' }, { value: 'coupon', label: 'Coupons' },
  { value: 'expense', label: 'Expenses' }, { value: 'settings', label: 'Settings' }, { value: 'review', label: 'Reviews' },
  { value: 'courier', label: 'Courier' }, { value: 'customer', label: 'Customers & groups' }, { value: 'collection', label: 'Collections' },
  { value: 'session', label: 'Sessions' }
];

@Component({
    selector: 'app-activity',
    imports: [CommonModule, FormsModule],
    template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">🕵️ Activity &amp; Sessions</h1>
      <p class="page-subtitle">Who changed what, and which staff devices are signed in. Visible to the shop owner only.</p>
    </div>
    <button class="btn-secondary" (click)="refresh()">↻ Refresh</button>
  </div>

  @if (errorMsg) {
    <div class="alert-error">❌ {{ errorMsg }}</div>
  }
  @if (successMsg) {
    <div class="alert-success">✅ {{ successMsg }}</div>
  }

  <div style="display:flex;gap:8px;margin-bottom:14px">
    <button [class]="tab === 'log' ? 'btn-primary' : 'btn-secondary'" (click)="tab = 'log'">Activity log</button>
    <button [class]="tab === 'sessions' ? 'btn-primary' : 'btn-secondary'" (click)="tab = 'sessions'; loadSessions()">Signed-in staff</button>
  </div>

  @if (tab === 'log') {
    <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:end;margin-bottom:14px">
      <div class="form-group" style="margin:0"><label>Area</label>
      <select class="form-input" [(ngModel)]="area" (change)="loadLog()">@for (a of areas; track a) {
        <option [value]="a.value">{{ a.label }}</option>
      }</select></div>
      <div class="form-group" style="margin:0"><label>From</label><input class="form-input" type="date" [(ngModel)]="from" (change)="loadLog()" /></div>
      <div class="form-group" style="margin:0"><label>To</label><input class="form-input" type="date" [(ngModel)]="to" (change)="loadLog()" /></div>
    </div>
    <div class="table-card">
      @if (isLoading) {
        <div class="loading">Loading…</div>
      }
      @if (!isLoading) {
        <table class="data-table">
          <thead><tr><th>When</th><th>Who</th><th>Action</th><th>Details</th><th>IP</th></tr></thead>
          <tbody>
            @for (r of rows; track r) {
              <tr>
                <td style="white-space:nowrap;font-size:13px">{{ r.date | date:'MMM d, h:mm a' }}</td>
                <td>{{ r.user || '—' }}</td>
                <td><span class="name-badge">{{ r.action }}</span></td>
                <td style="max-width:420px;word-break:break-word">{{ r.details || '—' }}</td>
                <td style="font-size:12.5px;opacity:.7">{{ r.ipAddress || '—' }}</td>
              </tr>
            }
            @if (rows.length === 0) {
              <tr><td colspan="5" class="empty-row">No activity recorded for this filter yet.</td></tr>
            }
          </tbody>
        </table>
      }
    </div>
    <p style="opacity:.6;font-size:12.5px;margin-top:8px">Setting changes list which settings were saved, never their values (so API keys never appear here).</p>
  }

  @if (tab === 'sessions') {
    <div class="table-card">
      <table class="data-table">
        <thead><tr><th>User</th><th>Signed in</th><th>Device</th><th>IP</th><th></th></tr></thead>
        <tbody>
          @for (s of sessions; track s) {
            <tr>
              <td><b>{{ s.fullName || s.email }}</b><div style="font-size:12.5px;opacity:.7">{{ s.email }}</div></td>
              <td style="font-size:13px">{{ s.createdAt | date:'MMM d, h:mm a' }}</td>
              <td style="font-size:12.5px;max-width:260px;word-break:break-word">{{ s.userAgent || '—' }}</td>
              <td style="font-size:12.5px">{{ s.ipAddress || '—' }}</td>
              <td class="actions">@if (s.current) {
                <span class="name-badge">This is you</span>
                }@if (!s.current) {
                <button class="btn-delete" (click)="revoke(s)">Sign out</button>
              }</td>
            </tr>
          }
          @if (sessions.length === 0) {
            <tr><td colspan="5" class="empty-row">No active sessions.</td></tr>
          }
        </tbody>
      </table>
    </div>
    <p style="opacity:.6;font-size:12.5px;margin-top:8px">A signed-out device is blocked within about 20 seconds.</p>
  }
</div>`
})
export class ActivityComponent implements OnInit {
  private svc = inject(CustomerAdminService);
  private confirmSvc = inject(ConfirmDialogService);

  areas = AREAS;
  tab: 'log' | 'sessions' = 'log';
  area = ''; from = ''; to = '';
  rows: ActivityRow[] = [];
  sessions: StaffSession[] = [];
  isLoading = false;
  errorMsg = ''; successMsg = '';

  ngOnInit() { this.loadLog(); }

  refresh() { this.tab === 'log' ? this.loadLog() : this.loadSessions(); }

  loadLog() {
    this.isLoading = true; this.errorMsg = '';
    this.svc.getLog({ action: this.area ? this.area + '.' : undefined, from: this.from || undefined, to: this.to || undefined }).subscribe({
      next: (r) => { this.rows = r; this.isLoading = false; },
      error: (e) => { this.isLoading = false; this.errorMsg = e?.status === 403 ? 'Only the shop owner can see the activity log.' : 'Could not load the activity log.'; }
    });
  }

  loadSessions() {
    this.errorMsg = '';
    this.svc.getSessions().subscribe({
      next: (s) => this.sessions = s,
      error: (e) => this.errorMsg = e?.status === 403 ? 'Only the shop owner can see sessions.' : 'Could not load sessions.'
    });
  }

  async revoke(s: StaffSession) {
    const ok = await this.confirmSvc.confirm({ title: 'Sign out device', message: `Sign ${s.email} out of this device?`, confirmLabel: 'Sign out', danger: true });
    if (!ok) return;
    this.svc.revokeSession(s.id).subscribe({
      next: () => { this.successMsg = 'Signed out.'; setTimeout(() => this.successMsg = '', 3000); this.loadSessions(); },
      error: (e) => this.errorMsg = e?.error?.message || 'Could not sign out that device.'
    });
  }
}
