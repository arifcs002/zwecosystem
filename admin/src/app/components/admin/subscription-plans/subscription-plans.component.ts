import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SubscriptionPlanService, SubscriptionPlan } from '../../../services/subscription-plan/subscription-plan.service';

interface CompanyOption { id: number; name: string; }

@Component({
  selector: 'app-subscription-plans',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './subscription-plans.component.html',
  styleUrl: './subscription-plans.component.css'
})
export class SubscriptionPlansComponent implements OnInit {
  private planService = inject(SubscriptionPlanService);

  plans: SubscriptionPlan[] = [];
  isLoading = false;
  successMsg = '';
  errorMsg = '';

  showEditModal = false;
  editingPlan: SubscriptionPlan | null = null;
  form: { name: string; price: number; billingCycle: string; featureList: string } = { name: '', price: 0, billingCycle: 'monthly', featureList: '' };

  ngOnInit() { this.load(); }

  load() {
    this.isLoading = true;
    this.planService.getAll().subscribe({
      next: (data) => { this.plans = data; this.isLoading = false; },
      error: () => { this.isLoading = false; }
    });
  }

  featuresOf(plan: SubscriptionPlan): { key: string; value: any }[] {
    if (!plan.features) return [];
    try {
      const obj = JSON.parse(plan.features);
      return Object.entries(obj).map(([key, value]) => ({ key, value }));
    } catch { return []; }
  }

  openCreate() {
    this.editingPlan = null;
    this.form = { name: '', price: 0, billingCycle: 'monthly', featureList: '' };
    this.errorMsg = '';
    this.showEditModal = true;
  }

  openEdit(plan: SubscriptionPlan) {
    this.editingPlan = plan;
    let featureList = '';
    if (plan.features) {
      try {
        const obj = JSON.parse(plan.features);
        featureList = Object.entries(obj).map(([k, v]) => `${k}=${v}`).join('\n');
      } catch { /* ignore malformed */ }
    }
    this.form = { name: plan.name, price: plan.price, billingCycle: plan.billingCycle, featureList };
    this.errorMsg = '';
    this.showEditModal = true;
  }

  closeModal() { this.showEditModal = false; }

  private buildFeaturesJson(): string {
    const lines = this.form.featureList.split('\n').map(l => l.trim()).filter(Boolean);
    const obj: Record<string, any> = {};
    for (const line of lines) {
      const [key, ...rest] = line.split('=');
      if (!key) continue;
      const raw = rest.join('=').trim();
      obj[key.trim()] = raw === 'true' ? true : raw === 'false' ? false : (isNaN(Number(raw)) ? raw : Number(raw));
    }
    return JSON.stringify(obj);
  }

  save() {
    if (!this.form.name.trim()) { this.errorMsg = 'Plan name is required.'; return; }
    const dto = {
      name: this.form.name,
      price: this.form.price,
      billingCycle: this.form.billingCycle,
      features: this.buildFeaturesJson()
    };
    const req = this.editingPlan
      ? this.planService.update(this.editingPlan.id, dto)
      : this.planService.create(dto);

    req.subscribe({
      next: () => {
        this.successMsg = this.editingPlan ? 'Plan updated.' : 'Plan created.';
        this.closeModal();
        this.load();
        setTimeout(() => this.successMsg = '', 4000);
      },
      error: (e) => { this.errorMsg = e?.error?.message || 'Failed to save plan.'; }
    });
  }

  remove(plan: SubscriptionPlan) {
    if (!confirm(`Delete plan "${plan.name}"?`)) return;
    this.planService.delete(plan.id).subscribe({
      next: () => { this.successMsg = 'Plan deleted.'; this.load(); setTimeout(() => this.successMsg = '', 4000); },
      error: (e) => { this.errorMsg = e?.error?.message || 'Failed to delete plan.'; }
    });
  }
}
