import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SubscriptionPlanService, SubscriptionPlan } from '../../../services/subscription-plan/subscription-plan.service';
import { BillingService, BillingSummary } from '../../../services/billing/billing.service';

@Component({
    selector: 'app-my-plan',
    imports: [CommonModule],
    templateUrl: './my-plan.component.html',
    styleUrl: './my-plan.component.css'
})
export class MyPlanComponent implements OnInit {
  private planService = inject(SubscriptionPlanService);
  private billingService = inject(BillingService);
  billing: BillingSummary | null = null;

  plan: SubscriptionPlan | null = null;
  usage: { products: number; users: number; orders: number } = { products: 0, users: 0, orders: 0 };
  isLoading = false;

  ngOnInit() {
    this.load();
    this.billingService.mine().subscribe({ next: b => this.billing = b, error: () => { /* the plan card above still works */ } });
  }

  statusLabel(): string {
    switch (this.billing?.status) {
      case 'expired': return 'Expired — changes paused';
      case 'grace': return 'Ended — renew soon';
      default: return 'Active';
    }
  }

  methodLabel(m: string): string { return ({ cash: 'Cash', bkash: 'bKash', nagad: 'Nagad', bank: 'Bank', other: 'Other' } as Record<string, string>)[m] || m; }

  load() {
    this.isLoading = true;
    this.planService.getMyPlan().subscribe({
      next: (data) => { this.plan = data.plan; this.usage = data.usage; this.isLoading = false; },
      error: () => { this.isLoading = false; }
    });
  }

  featuresOf(): { key: string; value: any }[] {
    if (!this.plan?.features) return [];
    try {
      const obj = JSON.parse(this.plan.features);
      return Object.entries(obj).map(([key, value]) => ({ key, value }));
    } catch { return []; }
  }

  limitFor(key: string): number | null {
    const f = this.featuresOf().find(x => x.key === key);
    return f && typeof f.value === 'number' ? f.value : null;
  }

  percentOf(used: number, limitKey: string): number {
    const limit = this.limitFor(limitKey);
    if (!limit) return 0;
    return Math.min(100, Math.round((used / limit) * 100));
  }
}
