import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Expense, ExpenseService, ExpenseUpsert } from '../../../services/expense/expense.service';
import { ConfirmDialogService } from '../../../services/confirm-dialog/confirm-dialog.service';

// Bangladesh calendar day (UTC+6), yyyy-MM-dd — matches how the profit report buckets days.
export const bdToday = () => new Date(Date.now() + 6 * 3600_000).toISOString().slice(0, 10);

@Component({
    selector: 'app-expense-management',
    imports: [CommonModule, FormsModule],
    template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">💸 Expenses</h1>
      <p class="page-subtitle">Rent, ads, salary, packaging — everything that isn't stock. Feeds your Profit &amp; Loss.</p>
    </div>
    <button class="btn-primary" (click)="openAdd()">+ Add Expense</button>
  </div>

  @if (successMsg) {
    <div class="alert-success">✅ {{ successMsg }}</div>
  }
  @if (errorMsg && !showModal) {
    <div class="alert-error">❌ {{ errorMsg }}</div>
  }

  <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:end;margin-bottom:14px">
    <div class="form-group" style="margin:0"><label>From</label><input class="form-input" type="date" [(ngModel)]="from" (change)="load()" /></div>
    <div class="form-group" style="margin:0"><label>To</label><input class="form-input" type="date" [(ngModel)]="to" (change)="load()" /></div>
    <div class="form-group" style="margin:0"><label>Category</label>
    <select class="form-input" [(ngModel)]="categoryFilter" (change)="load()">
      <option value="">All</option>@for (c of categories; track c) {
      <option [value]="c">{{ c }}</option>
    }
  </select>
</div>
<div style="margin-left:auto;font-size:15px">Total: <b>৳{{ total | number:'1.0-0' }}</b> <span style="opacity:.6">({{ rows.length }})</span></div>
</div>

<div class="table-card">
  @if (isLoading) {
    <div class="loading">Loading…</div>
  }
  @if (!isLoading) {
    <table class="data-table">
      <thead><tr><th>Date</th><th>Category</th><th>Amount</th><th>Paid to</th><th>Note</th><th>Actions</th></tr></thead>
      <tbody>
        @for (e of rows; track e) {
          <tr>
            <td>{{ e.expenseDate }}</td>
            <td><span class="name-badge">{{ e.category }}</span></td>
            <td><b>৳{{ e.amount | number:'1.0-2' }}</b></td>
            <td>{{ e.payee || '—' }}</td>
            <td style="max-width:280px">{{ e.note || '—' }}</td>
            <td class="actions">
              <button class="btn-edit" (click)="openEdit(e)">✏️ Edit</button>
              <button class="btn-delete" (click)="remove(e)">🗑️ Delete</button>
            </td>
          </tr>
        }
        @if (rows.length === 0) {
          <tr><td colspan="6" class="empty-row">No expenses in this range.</td></tr>
        }
      </tbody>
    </table>
  }
</div>

@if (showModal) {
  <div class="modal-overlay" (click)="closeModal()">
    <div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-header"><h2>{{ form.id ? 'Edit Expense' : 'Add Expense' }}</h2><button class="close-btn" (click)="closeModal()">✕</button></div>
      <div class="modal-body">
        @if (errorMsg) {
          <div class="alert-error">❌ {{ errorMsg }}</div>
        }
        <div class="form-group">
          <label>Category *</label>
          <input class="form-input" list="expense-cats" [(ngModel)]="form.category" name="category" maxlength="60" placeholder="Choose or type a new one" />
          <datalist id="expense-cats">@for (c of categories; track c) {
            <option [value]="c"></option>
          }</datalist>
        </div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px">
          <div class="form-group"><label>Amount (৳) *</label><input class="form-input" type="number" min="0" step="0.01" [(ngModel)]="form.amount" name="amount" /></div>
          <div class="form-group"><label>Date *</label><input class="form-input" type="date" [(ngModel)]="form.expenseDate" name="date" /></div>
        </div>
        <div class="form-group"><label>Paid to</label><input class="form-input" [(ngModel)]="form.payee" name="payee" maxlength="120" /></div>
        <div class="form-group"><label>Note</label><textarea class="form-input" rows="2" [(ngModel)]="form.note" name="note" maxlength="500"></textarea></div>
      </div>
      <div class="modal-footer">
        <button class="btn-secondary" (click)="closeModal()">Cancel</button>
        <button class="btn-primary" (click)="save()" [disabled]="saving">{{ saving ? 'Saving…' : 'Save' }}</button>
      </div>
    </div>
  </div>
}
</div>`
})
export class ExpenseManagementComponent implements OnInit {
  private svc = inject(ExpenseService);
  private confirmSvc = inject(ConfirmDialogService);

  rows: Expense[] = [];
  categories: string[] = [];
  from = bdToday().slice(0, 8) + '01';  // first of this month
  to = bdToday();
  categoryFilter = '';
  isLoading = false; saving = false; showModal = false;
  successMsg = ''; errorMsg = '';
  form: ExpenseUpsert & { id?: number } = this.empty();

  get total() { return this.rows.reduce((s, r) => s + r.amount, 0); }
  private empty() { return { category: '', amount: 0, expenseDate: bdToday(), payee: '', note: '' }; }

  ngOnInit() { this.svc.categories().subscribe(c => this.categories = c); this.load(); }

  load() {
    this.isLoading = true;
    this.svc.getAll({ from: this.from, to: this.to, category: this.categoryFilter }).subscribe({
      next: (r) => { this.rows = r; this.isLoading = false; },
      error: () => { this.isLoading = false; this.errorMsg = 'Could not load expenses.'; }
    });
  }

  openAdd() { this.form = this.empty(); this.errorMsg = ''; this.showModal = true; }
  openEdit(e: Expense) { this.form = { id: e.id, category: e.category, amount: e.amount, expenseDate: e.expenseDate, payee: e.payee || '', note: e.note || '' }; this.errorMsg = ''; this.showModal = true; }
  closeModal() { this.showModal = false; }

  save() {
    this.errorMsg = '';
    const f = this.form;
    if (!f.category.trim()) { this.errorMsg = 'Choose or type a category.'; return; }
    if (!f.amount || f.amount <= 0) { this.errorMsg = 'Enter an amount greater than 0.'; return; }
    if (!f.expenseDate) { this.errorMsg = 'Pick a date.'; return; }
    const dto: ExpenseUpsert = { category: f.category.trim(), amount: f.amount, expenseDate: f.expenseDate, payee: f.payee?.trim() || null, note: f.note?.trim() || null };
    this.saving = true;
    (f.id ? this.svc.update(f.id, dto) : this.svc.create(dto)).subscribe({
      next: () => { this.saving = false; this.showModal = false; this.flash('Expense saved.'); this.svc.categories().subscribe(c => this.categories = c); this.load(); },
      error: (e) => { this.saving = false; this.errorMsg = e?.error?.message || 'Could not save the expense.'; }
    });
  }

  async remove(e: Expense) {
    const ok = await this.confirmSvc.confirm({ title: 'Delete expense', message: `Delete ${e.category} ৳${e.amount} on ${e.expenseDate}?`, confirmLabel: 'Delete', danger: true });
    if (!ok) return;
    this.svc.remove(e.id).subscribe({ next: () => { this.flash('Expense deleted.'); this.load(); }, error: () => this.errorMsg = 'Could not delete.' });
  }

  private flash(m: string) { this.successMsg = m; setTimeout(() => this.successMsg = '', 3000); }
}
