import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { PricingTagService, PricingTag } from '../../../services/pricing-tag/pricing-tag.service';
import { SettingsService } from '../../../services/settings/settings.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';

@Component({
  selector: 'app-pricing-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './pricing-management.component.html',
  styleUrl: './pricing-management.component.css'
})
export class PricingManagementComponent implements OnInit {
  private pricingTagService = inject(PricingTagService);
  private settingsService = inject(SettingsService);
  private notify = inject(GlobalNotificationService);

  tags: PricingTag[] = [];
  isLoading = false;

  showModal = false;
  isEditMode = false;
  saving = false;
  submitted = false;
  errorMsg = '';
  currentTag: PricingTag = this.getEmpty();

  tagToDelete: PricingTag | null = null;
  showDeleteModal = false;
  deleting = false;

  // Per-company delivery charge — stored as a Settings key (not on the Company
  // entity), set here instead of at company creation.
  deliveryCharge = 0;
  savingDelivery = false;

  ngOnInit() { this.load(); }

  load() {
    this.isLoading = true;
    forkJoin({
      tags: this.pricingTagService.getPricingTags(),
      settings: this.settingsService.getSettings()
    }).subscribe({
      next: ({ tags, settings }) => {
        this.tags = tags.sort((a, b) => a.name.localeCompare(b.name));
        const dc = settings.find(s => s.key === 'delivery_charge');
        this.deliveryCharge = dc?.value ? Number(dc.value) : 0;
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  saveDeliveryCharge() {
    this.savingDelivery = true;
    this.settingsService.updateSettings([
      { key: 'delivery_charge', value: this.deliveryCharge.toString(), groupName: 'ECOMMERCE' }
    ]).subscribe({
      next: () => {
        this.savingDelivery = false;
        this.notify.notify({ type: 'success', title: 'Saved', message: 'Delivery charge updated.', ttlMs: 3000 });
      },
      error: () => {
        this.savingDelivery = false;
        this.notify.notify({ type: 'error', title: 'Failed', message: 'Could not save delivery charge.', ttlMs: 5000 });
      }
    });
  }

  openNew() {
    this.currentTag = this.getEmpty();
    this.submitted = false;
    this.errorMsg = '';
    this.isEditMode = false;
    this.showModal = true;
  }

  openEdit(tag: PricingTag) {
    this.currentTag = {
      ...tag,
      promoStartDate: tag.promoStartDate ? tag.promoStartDate.substring(0, 10) : null,
      promoEndDate: tag.promoEndDate ? tag.promoEndDate.substring(0, 10) : null
    };
    this.submitted = false;
    this.errorMsg = '';
    this.isEditMode = true;
    this.showModal = true;
  }

  closeModal() { this.showModal = false; }

  save() {
    this.submitted = true;
    this.errorMsg = '';
    if (!this.currentTag.name?.trim()) {
      this.errorMsg = 'Tag name is required.';
      return;
    }
    if (this.currentTag.profitPercent == null || this.currentTag.profitPercent < 0) {
      this.errorMsg = 'Profit % must be 0 or more.';
      return;
    }
    this.saving = true;
    const action$ = this.isEditMode
      ? this.pricingTagService.updatePricingTag(this.currentTag.id!, this.currentTag)
      : this.pricingTagService.createPricingTag(this.currentTag);

    action$.subscribe({
      next: () => {
        this.notify.notify({ type: 'success', title: this.isEditMode ? 'Updated' : 'Created', message: `Pricing tag "${this.currentTag.name}" saved.`, ttlMs: 3000 });
        this.saving = false;
        this.showModal = false;
        this.load();
      },
      error: (err) => {
        this.saving = false;
        this.errorMsg = err.error?.message || 'Failed to save pricing tag.';
      }
    });
  }

  confirmDelete(tag: PricingTag) {
    this.tagToDelete = tag;
    this.showDeleteModal = true;
  }

  cancelDelete() { this.showDeleteModal = false; this.tagToDelete = null; }

  doDelete() {
    if (!this.tagToDelete?.id) return;
    this.deleting = true;
    this.pricingTagService.deletePricingTag(this.tagToDelete.id).subscribe({
      next: () => {
        this.notify.notify({ type: 'success', title: 'Deleted', message: `Pricing tag "${this.tagToDelete!.name}" deleted.`, ttlMs: 3000 });
        this.tags = this.tags.filter(t => t.id !== this.tagToDelete!.id);
        this.deleting = false;
        this.showDeleteModal = false;
        this.tagToDelete = null;
      },
      error: () => {
        this.deleting = false;
        this.notify.notify({ type: 'error', title: 'Delete failed', message: 'Could not delete pricing tag.', ttlMs: 5000 });
      }
    });
  }

  getEmpty(): PricingTag {
    return { name: '', profitPercent: 0, discountPercent: null, promoStartDate: null, promoEndDate: null, isActive: true };
  }

  // Sell price preview for the modal: buy price * (1 + profit% / 100)
  previewSellPrice(buyPrice: number): number {
    return Math.round(buyPrice * (1 + (this.currentTag.profitPercent || 0) / 100));
  }

  isPromoActive(tag: PricingTag): boolean {
    if (!tag.discountPercent || !tag.promoStartDate || !tag.promoEndDate) return false;
    const now = new Date();
    return now >= new Date(tag.promoStartDate) && now <= new Date(tag.promoEndDate);
  }
}
