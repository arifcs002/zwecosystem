import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Coupon, CouponService, CouponUpsert } from '../../../services/coupon/coupon.service';
import { ConfirmDialogService } from '../../../services/confirm-dialog/confirm-dialog.service';

interface Form {
  id?: number; code: string; description: string; discountType: 'PERCENT' | 'FIXED';
  discountValue: number | null; minOrderAmount: number | null; maxDiscountAmount: number | null;
  usageLimit: number | null; startsAt: string; expiresAt: string; isActive: boolean;
}

// <input type="datetime-local"> works in local time without a zone; the API stores UTC.
const toLocalInput = (iso?: string | null) => {
  if (!iso) return '';
  const d = new Date(iso);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
};

@Component({
  selector: 'app-coupon-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">🏷️ Coupons</h1>
      <p class="page-subtitle">Discount codes your customers can apply at checkout</p>
    </div>
    <button class="btn-primary" (click)="openAdd()">+ New Coupon</button>
  </div>

  <div class="alert-success" *ngIf="successMsg">✅ {{ successMsg }}</div>
  <div class="alert-error" *ngIf="errorMsg && !showModal">❌ {{ errorMsg }}</div>

  <div class="table-card">
    <div class="loading" *ngIf="isLoading">Loading coupons...</div>
    <table class="data-table" *ngIf="!isLoading">
      <thead>
        <tr><th>Code</th><th>Discount</th><th>Min. order</th><th>Used</th><th>Validity</th><th>Status</th><th>Actions</th></tr>
      </thead>
      <tbody>
        <tr *ngFor="let c of coupons">
          <td><span class="name-badge">{{ c.code }}</span><div *ngIf="c.description" style="font-size:12px;opacity:.65;margin-top:4px">{{ c.description }}</div></td>
          <td>
            <ng-container *ngIf="c.discountType === 'PERCENT'">{{ c.discountValue }}% off<span *ngIf="c.maxDiscountAmount" style="opacity:.65"> (max ৳{{ c.maxDiscountAmount }})</span></ng-container>
            <ng-container *ngIf="c.discountType === 'FIXED'">৳{{ c.discountValue }} off</ng-container>
          </td>
          <td>{{ c.minOrderAmount ? '৳' + c.minOrderAmount : '—' }}</td>
          <td>{{ c.usedCount }}{{ c.usageLimit ? ' / ' + c.usageLimit : '' }}</td>
          <td style="font-size:13px">
            <span *ngIf="!c.startsAt && !c.expiresAt">Always</span>
            <span *ngIf="c.startsAt">From {{ c.startsAt | date:'mediumDate' }}</span>
            <span *ngIf="c.startsAt && c.expiresAt"> · </span>
            <span *ngIf="c.expiresAt">Until {{ c.expiresAt | date:'mediumDate' }}</span>
          </td>
          <td>
            <span class="name-badge" [style.opacity]="state(c) === 'Active' ? 1 : .6">{{ state(c) }}</span>
          </td>
          <td class="actions">
            <button class="btn-edit" (click)="openEdit(c)">✏️ Edit</button>
            <button class="btn-edit" (click)="toggle(c)">{{ c.isActive ? '⏸ Disable' : '▶ Enable' }}</button>
            <button class="btn-delete" (click)="remove(c)">🗑️ Delete</button>
          </td>
        </tr>
        <tr *ngIf="coupons.length === 0"><td colspan="7" class="empty-row">No coupons yet. Create one to run a promotion.</td></tr>
      </tbody>
    </table>
  </div>

  <div class="modal-overlay" *ngIf="showModal" (click)="closeModal()">
    <div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-header">
        <h2>{{ form.id ? 'Edit Coupon' : 'New Coupon' }}</h2>
        <button class="close-btn" (click)="closeModal()">✕</button>
      </div>
      <div class="modal-body">
        <div class="alert-error" *ngIf="errorMsg">❌ {{ errorMsg }}</div>
        <div class="form-group">
          <label>Code *</label>
          <input class="form-input" [(ngModel)]="form.code" name="code" maxlength="32" placeholder="EID100" style="text-transform:uppercase" />
          <small style="opacity:.6">Letters, numbers, - or _ (3–32 characters). Customers type this at checkout.</small>
        </div>
        <div class="form-group">
          <label>Description (internal)</label>
          <input class="form-input" [(ngModel)]="form.description" name="description" placeholder="Eid campaign" />
        </div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px">
          <div class="form-group">
            <label>Type *</label>
            <select class="form-input" [(ngModel)]="form.discountType" name="type">
              <option value="PERCENT">Percentage (%)</option>
              <option value="FIXED">Fixed amount (৳)</option>
            </select>
          </div>
          <div class="form-group">
            <label>{{ form.discountType === 'PERCENT' ? 'Percent off *' : 'Amount off (৳) *' }}</label>
            <input class="form-input" type="number" min="0" [(ngModel)]="form.discountValue" name="value" />
          </div>
          <div class="form-group">
            <label>Minimum order (৳)</label>
            <input class="form-input" type="number" min="0" [(ngModel)]="form.minOrderAmount" name="min" placeholder="0" />
          </div>
          <div class="form-group" *ngIf="form.discountType === 'PERCENT'">
            <label>Max discount (৳)</label>
            <input class="form-input" type="number" min="0" [(ngModel)]="form.maxDiscountAmount" name="max" placeholder="No cap" />
          </div>
          <div class="form-group">
            <label>Total usage limit</label>
            <input class="form-input" type="number" min="1" [(ngModel)]="form.usageLimit" name="limit" placeholder="Unlimited" />
          </div>
          <div class="form-group">
            <label>Starts</label>
            <input class="form-input" type="datetime-local" [(ngModel)]="form.startsAt" name="starts" />
          </div>
          <div class="form-group">
            <label>Expires</label>
            <input class="form-input" type="datetime-local" [(ngModel)]="form.expiresAt" name="expires" />
          </div>
        </div>
        <label style="display:flex;align-items:center;gap:8px;margin-top:6px"><input type="checkbox" [(ngModel)]="form.isActive" name="active" /> Active</label>
      </div>
      <div class="modal-footer">
        <button class="btn-secondary" (click)="closeModal()">Cancel</button>
        <button class="btn-primary" (click)="save()" [disabled]="saving">{{ saving ? 'Saving…' : (form.id ? 'Update' : 'Create Coupon') }}</button>
      </div>
    </div>
  </div>
</div>`
})
export class CouponManagementComponent implements OnInit {
  private svc = inject(CouponService);
  private confirmSvc = inject(ConfirmDialogService);

  coupons: Coupon[] = [];
  isLoading = false;
  saving = false;
  showModal = false;
  successMsg = '';
  errorMsg = '';
  form: Form = this.empty();

  private empty(): Form {
    return { code: '', description: '', discountType: 'PERCENT', discountValue: null, minOrderAmount: null,
      maxDiscountAmount: null, usageLimit: null, startsAt: '', expiresAt: '', isActive: true };
  }

  ngOnInit() { this.load(); }

  load() {
    this.isLoading = true;
    this.svc.getAll().subscribe({
      next: (rows) => { this.coupons = rows; this.isLoading = false; },
      error: () => { this.isLoading = false; this.errorMsg = 'Could not load coupons.'; }
    });
  }

  state(c: Coupon): string {
    const now = Date.now();
    if (!c.isActive) return 'Disabled';
    if (c.expiresAt && now > new Date(c.expiresAt).getTime()) return 'Expired';
    if (c.startsAt && now < new Date(c.startsAt).getTime()) return 'Scheduled';
    if (c.usageLimit && c.usedCount >= c.usageLimit) return 'Used up';
    return 'Active';
  }

  openAdd() { this.form = this.empty(); this.errorMsg = ''; this.showModal = true; }

  openEdit(c: Coupon) {
    this.form = {
      id: c.id, code: c.code, description: c.description || '', discountType: c.discountType,
      discountValue: c.discountValue, minOrderAmount: c.minOrderAmount || null,
      maxDiscountAmount: c.maxDiscountAmount ?? null, usageLimit: c.usageLimit ?? null,
      startsAt: toLocalInput(c.startsAt), expiresAt: toLocalInput(c.expiresAt), isActive: c.isActive
    };
    this.errorMsg = ''; this.showModal = true;
  }

  closeModal() { this.showModal = false; }

  save() {
    const f = this.form;
    this.errorMsg = '';
    if (!f.code.trim()) { this.errorMsg = 'Enter a coupon code.'; return; }
    if (!f.discountValue || f.discountValue <= 0) { this.errorMsg = 'Enter a discount value greater than 0.'; return; }
    if (f.discountType === 'PERCENT' && f.discountValue > 100) { this.errorMsg = 'A percentage cannot be more than 100.'; return; }
    if (f.startsAt && f.expiresAt && new Date(f.expiresAt) <= new Date(f.startsAt)) { this.errorMsg = 'Expiry must be after the start date.'; return; }

    const dto: CouponUpsert = {
      code: f.code.trim().toUpperCase(), description: f.description.trim() || null, discountType: f.discountType,
      discountValue: f.discountValue, minOrderAmount: f.minOrderAmount || 0,
      maxDiscountAmount: f.discountType === 'PERCENT' ? (f.maxDiscountAmount || null) : null,
      usageLimit: f.usageLimit || null,
      startsAt: f.startsAt ? new Date(f.startsAt).toISOString() : null,
      expiresAt: f.expiresAt ? new Date(f.expiresAt).toISOString() : null,
      isActive: f.isActive
    };
    this.saving = true;
    const req = f.id ? this.svc.update(f.id, dto) : this.svc.create(dto);
    req.subscribe({
      next: () => {
        this.saving = false; this.showModal = false; this.load();
        this.flash(f.id ? 'Coupon updated.' : 'Coupon created.');
      },
      error: (e) => { this.saving = false; this.errorMsg = e?.error?.message || 'Could not save the coupon.'; }
    });
  }

  toggle(c: Coupon) {
    this.svc.toggle(c.id).subscribe({ next: () => this.load(), error: () => this.errorMsg = 'Could not change the status.' });
  }

  async remove(c: Coupon) {
    const ok = await this.confirmSvc.confirm({ title: 'Delete coupon', message: `Delete coupon ${c.code}? Past orders keep their discount.`, confirmLabel: 'Delete', danger: true });
    if (!ok) return;
    this.svc.remove(c.id).subscribe({ next: () => { this.flash('Coupon deleted.'); this.load(); }, error: () => this.errorMsg = 'Could not delete the coupon.' });
  }

  private flash(msg: string) { this.successMsg = msg; setTimeout(() => this.successMsg = '', 3000); }
}
