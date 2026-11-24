import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { BillingService, BillingSummary } from '../../../services/billing/billing.service';
import { SubscriptionPlanService, SubscriptionPlan } from '../../../services/subscription-plan/subscription-plan.service';
import { CompanyService, Company } from '../../../services/company/company.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';
import { environment } from '../../../../environments/environment';

@Component({
    selector: 'app-company-management',
    imports: [CommonModule, FormsModule],
    templateUrl: './company-management.component.html',
    styleUrl: './company-management.component.css'
})
export class CompanyManagementComponent implements OnInit {
  private router = inject(Router);
  private companyService = inject(CompanyService);
  private notify = inject(GlobalNotificationService);
  private billingService = inject(BillingService);
  private planService = inject(SubscriptionPlanService);

  // ── Billing / manual payments ──
  billingCompany: Company | null = null;
  billing: BillingSummary | null = null;
  plans: SubscriptionPlan[] = [];
  pay = { planId: null as number | null, amount: 0, method: 'bkash', reference: '', months: 1, note: '' };
  paying = false;
  payError = '';

  openBilling(c: Company) {
    this.billingCompany = c; this.billing = null; this.payError = '';
    this.pay = { planId: null, amount: 0, method: 'bkash', reference: '', months: 1, note: '' };
    this.billingService.forCompany(c.id).subscribe({
      next: b => { this.billing = b; this.pay.planId = b.plan?.id ?? null; this.suggestAmount(); },
      error: () => { this.payError = 'Could not load billing.'; }
    });
    if (!this.plans.length) this.planService.getAll().subscribe({ next: p => { this.plans = p; this.suggestAmount(); } });
  }

  // Fills the amount from the plan price × months; the owner can still type a different figure.
  suggestAmount() {
    const plan = this.plans.find(p => p.id === this.pay.planId);
    if (plan) this.pay.amount = Math.round(plan.price * this.pay.months);
  }

  savePayment() {
    if (!this.billingCompany || this.paying) return;
    this.paying = true; this.payError = '';
    this.billingService.recordPayment(this.billingCompany.id, {
      planId: this.pay.planId, amount: this.pay.amount, method: this.pay.method,
      reference: this.pay.reference || undefined, months: this.pay.months, note: this.pay.note || undefined
    }).subscribe({
      next: b => {
        this.paying = false; this.billing = b;
        this.notify.notify({ type: 'success', title: 'Payment recorded', message: 'Subscription extended.', ttlMs: 4000 });
        this.pay.reference = ''; this.pay.note = '';
      },
      error: (e) => { this.paying = false; this.payError = e?.error?.message || 'Could not record the payment.'; }
    });
  }

  companies: Company[] = [];
  loading = false;
  companyToDelete: Company | null = null;
  deleteLoading = false;
  readonly rootDomain = environment.rootDomain;

  storeUrl(subdomain: string): string { return `http://${subdomain}.${this.rootDomain}`; }

  get activeCount() { return this.companies.filter(c => c.isActive).length; }
  get pendingCount() { return this.companies.filter(c => c.approvalStatus === 'Pending').length; }
  get approvedCount() { return this.companies.filter(c => c.approvalStatus === 'Approved').length; }

  ngOnInit() { this.loadCompanies(); }

  loadCompanies() {
    this.loading = true;
    this.companyService.getCompanies().subscribe({
      next: (data) => { this.companies = data; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

  navigateToCreate() { this.router.navigate(['/admin/company/create']); }
  navigateToEdit(company: Company) { this.router.navigate(['/admin/company/edit', company.id]); }

  toggleStatus(company: Company) {
    this.companyService.toggleStatus(company.id).subscribe({
      next: (res) => {
        company.isActive = res.isActive;
        this.notify.notify({ type: 'success', title: 'Status updated', message: res.message, ttlMs: 4000 });
      },
      error: () => {}
    });
  }

  confirmDelete(company: Company) { this.companyToDelete = company; }

  deleteCompany() {
    if (!this.companyToDelete) return;
    this.deleteLoading = true;
    this.companyService.deleteCompany(this.companyToDelete.id).subscribe({
      next: (res) => {
        this.companies = this.companies.filter(c => c.id !== this.companyToDelete!.id);
        this.notify.notify({ type: 'success', title: 'Deleted', message: res.message, ttlMs: 4000 });
        this.companyToDelete = null;
        this.deleteLoading = false;
      },
      error: () => { this.deleteLoading = false; }
    });
  }
}
