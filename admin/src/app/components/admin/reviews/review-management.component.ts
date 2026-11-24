import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AdminReview, ReviewService, ReviewStatus } from '../../../services/review/review.service';
import { ConfirmDialogService } from '../../../services/confirm-dialog/confirm-dialog.service';

const TABS: { value: ReviewStatus | ''; label: string }[] = [
  { value: 'PENDING', label: 'Waiting for approval' }, { value: 'APPROVED', label: 'Published' },
  { value: 'REJECTED', label: 'Rejected' }, { value: '', label: 'All' }
];

@Component({
    selector: 'app-review-management',
    imports: [CommonModule],
    template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">⭐ Reviews</h1>
      <p class="page-subtitle">Approve what customers say about your products. Verified buyers are published automatically.</p>
    </div>
    <button class="btn-secondary" (click)="load()">↻ Refresh</button>
  </div>

  @if (errorMsg) {
    <div class="alert-error">❌ {{ errorMsg }}</div>
  }
  <div style="display:flex;gap:8px;flex-wrap:wrap;margin-bottom:14px">
    @for (t of tabs; track t) {
      <button (click)="select(t.value)" [class]="status === t.value ? 'btn-primary' : 'btn-secondary'">{{ t.label }}</button>
    }
  </div>

  <div class="table-card">
    @if (isLoading) {
      <div class="loading">Loading…</div>
    }
    @if (!isLoading) {
      <table class="data-table">
        <thead><tr><th>Product</th><th>Rating</th><th>Review</th><th>Status</th><th>Actions</th></tr></thead>
        <tbody>
          @for (r of rows; track r) {
            <tr>
              <td><b>{{ r.productName }}</b><div style="font-size:12px;opacity:.65">{{ r.date | date:'mediumDate' }}</div></td>
              <td style="white-space:nowrap;color:#fbbf24">{{ stars(r.rating) }}</td>
              <td style="max-width:360px">
                <div><b>{{ r.reviewerName }}</b> @if (r.isVerified) {
                <span class="name-badge" style="margin-left:4px">✓ Verified buyer</span>
              }</div>
              <div style="opacity:.85;margin-top:3px;white-space:pre-line">{{ r.comment || '—' }}</div>
            </td>
            <td><span class="name-badge">{{ r.status === 'PENDING' ? 'Waiting' : r.status === 'APPROVED' ? 'Published' : 'Rejected' }}</span></td>
            <td class="actions">
              @if (r.status !== 'APPROVED') {
                <button class="btn-edit" (click)="setStatus(r, 'APPROVED')">✓ Approve</button>
              }
              @if (r.status !== 'REJECTED') {
                <button class="btn-edit" (click)="setStatus(r, 'REJECTED')">✕ Reject</button>
              }
              <button class="btn-delete" (click)="remove(r)">🗑️</button>
            </td>
          </tr>
        }
        @if (rows.length === 0) {
          <tr><td colspan="5" class="empty-row">No reviews here yet.</td></tr>
        }
      </tbody>
    </table>
  }
</div>
</div>`
})
export class ReviewManagementComponent implements OnInit {
  private svc = inject(ReviewService);
  private confirmSvc = inject(ConfirmDialogService);

  tabs = TABS;
  status: ReviewStatus | '' = 'PENDING';
  rows: AdminReview[] = [];
  isLoading = false;
  errorMsg = '';

  ngOnInit() { this.load(); }
  select(s: ReviewStatus | '') { this.status = s; this.load(); }
  stars(n: number) { return '★'.repeat(n) + '☆'.repeat(5 - n); }

  load() {
    this.isLoading = true; this.errorMsg = '';
    this.svc.getAll(this.status).subscribe({
      next: (r) => { this.rows = r; this.isLoading = false; },
      error: () => { this.isLoading = false; this.errorMsg = 'Could not load reviews.'; }
    });
  }

  setStatus(r: AdminReview, s: ReviewStatus) {
    this.svc.setStatus(r.id, s).subscribe({ next: () => this.load(), error: () => this.errorMsg = 'Could not update the review.' });
  }

  async remove(r: AdminReview) {
    const ok = await this.confirmSvc.confirm({ title: 'Delete review', message: `Delete ${r.reviewerName}'s review?`, confirmLabel: 'Delete', danger: true });
    if (!ok) return;
    this.svc.remove(r.id).subscribe({ next: () => this.load(), error: () => this.errorMsg = 'Could not delete.' });
  }
}
