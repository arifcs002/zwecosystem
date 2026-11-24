import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CustomerAccountRow, CustomerAdminService, CustomerGroup } from '../../../services/customer-admin/customer-admin.service';
import { ConfirmDialogService } from '../../../services/confirm-dialog/confirm-dialog.service';

@Component({
    selector: 'app-customer-accounts',
    imports: [CommonModule, FormsModule],
    template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">👥 Customer Accounts &amp; Groups</h1>
      <p class="page-subtitle">Shoppers who signed up on your store. Put them in a group to give them an automatic discount.</p>
    </div>
    <button class="btn-primary" (click)="openGroup(null)">+ New Group</button>
  </div>

  @if (successMsg) {
    <div class="alert-success">✅ {{ successMsg }}</div>
  }
  @if (errorMsg && !showModal) {
    <div class="alert-error">❌ {{ errorMsg }}</div>
  }

  <p style="opacity:.7;font-size:13px;margin:0 0 12px">A member's group discount and a coupon do not add up — the customer gets whichever is bigger. Accounts are not phone-verified, so groups are assigned here by you, never automatically.</p>

  <div style="display:flex;gap:10px;flex-wrap:wrap;margin-bottom:16px">
    @for (g of groups; track g) {
      <div class="table-card" style="padding:12px 16px;min-width:200px">
        <div style="font-weight:700">{{ g.name }}</div>
        <div style="font-size:22px;font-weight:800;color:#34d399">{{ g.discountPercent }}% off</div>
        <div style="opacity:.65;font-size:12.5px">{{ g.members }} member{{ g.members === 1 ? '' : 's' }}@if (g.description) {
          <span> · {{ g.description }}</span>
        }</div>
        <div style="margin-top:8px"><button class="btn-edit" (click)="openGroup(g)">✏️ Edit</button> <button class="btn-delete" (click)="removeGroup(g)">🗑️</button></div>
      </div>
    }
    @if (groups.length === 0) {
      <div style="opacity:.6;padding:8px 0">No groups yet.</div>
    }
  </div>

  <div class="form-group" style="max-width:340px"><input class="form-input" [(ngModel)]="search" (input)="loadAccounts()" placeholder="🔍 Search by name or phone" /></div>

  <div class="table-card">
    @if (isLoading) {
      <div class="loading">Loading…</div>
    }
    @if (!isLoading) {
      <table class="data-table">
        <thead><tr><th>Customer</th><th>Orders</th><th>Joined</th><th>Group</th><th>Status</th></tr></thead>
        <tbody>
          @for (a of accounts; track a) {
            <tr>
              <td><b>{{ a.name }}</b><div style="font-size:13px;opacity:.75">{{ a.phone }}@if (a.email) {
              <span> · {{ a.email }}</span>
            }</div></td>
            <td>{{ a.orders }}</td>
            <td style="font-size:13px">{{ a.createdDate | date:'mediumDate' }}</td>
            <td>
              <select class="form-input" style="min-width:140px" [ngModel]="a.groupId ?? ''" (ngModelChange)="assign(a, $event)">
                <option value="">— none —</option>
                @for (g of groups; track g) {
                  <option [value]="g.id">{{ g.name }} ({{ g.discountPercent }}%)</option>
                }
              </select>
            </td>
            <td class="actions"><button [class]="a.isActive ? 'btn-edit' : 'btn-delete'" (click)="toggleActive(a)">{{ a.isActive ? '✓ Active — disable' : '⛔ Disabled — enable' }}</button></td>
          </tr>
        }
        @if (accounts.length === 0) {
          <tr><td colspan="5" class="empty-row">No customer accounts yet.</td></tr>
        }
      </tbody>
    </table>
  }
</div>

@if (showModal) {
  <div class="modal-overlay" (click)="showModal = false">
    <div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-header"><h2>{{ form.id ? 'Edit Group' : 'New Group' }}</h2><button class="close-btn" (click)="showModal = false">✕</button></div>
      <div class="modal-body">
        @if (errorMsg) {
          <div class="alert-error">❌ {{ errorMsg }}</div>
        }
        <div class="form-group"><label>Name *</label><input class="form-input" [(ngModel)]="form.name" name="gname" maxlength="60" placeholder="VIP" /></div>
        <div class="form-group"><label>Discount (%) *</label><input class="form-input" type="number" min="0" max="100" step="0.5" [(ngModel)]="form.discountPercent" name="gpct" /></div>
        <div class="form-group"><label>Description</label><input class="form-input" [(ngModel)]="form.description" name="gdesc" maxlength="300" /></div>
      </div>
      <div class="modal-footer"><button class="btn-secondary" (click)="showModal = false">Cancel</button><button class="btn-primary" (click)="saveGroup()">Save</button></div>
    </div>
  </div>
}
</div>`
})
export class CustomerAccountsComponent implements OnInit {
  private svc = inject(CustomerAdminService);
  private confirmSvc = inject(ConfirmDialogService);

  groups: CustomerGroup[] = [];
  accounts: CustomerAccountRow[] = [];
  search = '';
  isLoading = false; showModal = false;
  successMsg = ''; errorMsg = '';
  form: { id: number | null; name: string; description: string; discountPercent: number } = { id: null, name: '', description: '', discountPercent: 0 };

  ngOnInit() { this.loadGroups(); this.loadAccounts(); }

  loadGroups() { this.svc.getGroups().subscribe({ next: g => this.groups = g, error: () => this.errorMsg = 'Could not load groups.' }); }

  loadAccounts() {
    this.isLoading = true;
    this.svc.getAccounts(this.search.trim()).subscribe({
      next: (a) => { this.accounts = a; this.isLoading = false; },
      error: () => { this.isLoading = false; this.errorMsg = 'Could not load customers.'; }
    });
  }

  openGroup(g: CustomerGroup | null) {
    this.errorMsg = '';
    this.form = g ? { id: g.id, name: g.name, description: g.description || '', discountPercent: g.discountPercent } : { id: null, name: '', description: '', discountPercent: 10 };
    this.showModal = true;
  }

  saveGroup() {
    this.errorMsg = '';
    if (this.form.name.trim().length < 2) { this.errorMsg = 'Enter a group name.'; return; }
    if (this.form.discountPercent < 0 || this.form.discountPercent > 100) { this.errorMsg = 'Discount must be between 0 and 100.'; return; }
    this.svc.saveGroup(this.form.id, { name: this.form.name.trim(), description: this.form.description.trim() || null, discountPercent: this.form.discountPercent }).subscribe({
      next: () => { this.showModal = false; this.flash('Group saved.'); this.loadGroups(); this.loadAccounts(); },
      error: (e) => this.errorMsg = e?.error?.message || 'Could not save the group.'
    });
  }

  async removeGroup(g: CustomerGroup) {
    const ok = await this.confirmSvc.confirm({ title: 'Delete group', message: `Delete "${g.name}"? Its ${g.members} member(s) will lose the discount.`, confirmLabel: 'Delete', danger: true });
    if (!ok) return;
    this.svc.deleteGroup(g.id).subscribe({ next: () => { this.flash('Group deleted.'); this.loadGroups(); this.loadAccounts(); }, error: () => this.errorMsg = 'Could not delete the group.' });
  }

  assign(a: CustomerAccountRow, value: string | number) {
    const groupId = value === '' || value === null ? null : Number(value);
    this.svc.assignGroup(a.id, groupId).subscribe({
      next: () => { this.flash('Group updated.'); this.loadGroups(); this.loadAccounts(); },
      error: (e) => { this.errorMsg = e?.error?.message || 'Could not change the group.'; this.loadAccounts(); }
    });
  }

  toggleActive(a: CustomerAccountRow) {
    this.svc.setActive(a.id, !a.isActive).subscribe({ next: () => this.loadAccounts(), error: () => this.errorMsg = 'Could not change the account.' });
  }

  private flash(m: string) { this.successMsg = m; setTimeout(() => this.successMsg = '', 3000); }
}
