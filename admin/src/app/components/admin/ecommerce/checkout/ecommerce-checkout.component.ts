import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, CompanySetting } from '../../../../services/settings/settings.service';
import { GlobalNotificationService } from '../../../../services/global-notification/global-notification.service';

@Component({
  selector: 'app-ecommerce-checkout',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './ecommerce-checkout.component.html',
  styleUrl: '../ecommerce-config.css'
})
export class EcommerceCheckoutComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private notify = inject(GlobalNotificationService);

  paymentCod = true;
  paymentBkash = false;
  bkashNumber = '';
  paymentOnline = false;
  deliveryCharge = 0;
  freeDeliveryAbove = 0;

  isLoading = false;
  saving = false;

  ngOnInit() {
    this.isLoading = true;
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        settings.forEach(s => {
          if (s.key === 'payment_cod') this.paymentCod = s.value === 'true';
          if (s.key === 'payment_bkash') this.paymentBkash = s.value === 'true';
          if (s.key === 'bkash_number') this.bkashNumber = s.value;
          if (s.key === 'payment_online') this.paymentOnline = s.value === 'true';
          if (s.key === 'delivery_charge' && s.value) this.deliveryCharge = +s.value;
          if (s.key === 'free_delivery_above' && s.value) this.freeDeliveryAbove = +s.value;
        });
        // Default COD on if nothing was ever saved.
        if (!settings.some(s => s.key === 'payment_cod')) this.paymentCod = true;
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  save() {
    if (!this.paymentCod && !this.paymentBkash && !this.paymentOnline) {
      this.notify.notify({ type: 'warning', title: 'Pick a method', message: 'Enable at least one payment method.', ttlMs: 4000 });
      return;
    }
    this.saving = true;
    const settings: CompanySetting[] = [
      { key: 'payment_cod', value: this.paymentCod ? 'true' : 'false', groupName: 'CHECKOUT' },
      { key: 'payment_bkash', value: this.paymentBkash ? 'true' : 'false', groupName: 'CHECKOUT' },
      { key: 'bkash_number', value: this.bkashNumber, groupName: 'CHECKOUT' },
      { key: 'payment_online', value: this.paymentOnline ? 'true' : 'false', groupName: 'CHECKOUT' },
      { key: 'delivery_charge', value: String(this.deliveryCharge || 0), groupName: 'CHECKOUT' },
      { key: 'free_delivery_above', value: String(this.freeDeliveryAbove || 0), groupName: 'CHECKOUT' }
    ];
    this.settingsService.updateSettings(settings).subscribe({
      next: () => { this.saving = false; this.notify.notify({ type: 'success', title: 'Saved', message: 'Checkout settings saved.', ttlMs: 3000 }); },
      error: () => { this.saving = false; this.notify.notify({ type: 'error', title: 'Failed', message: 'Could not save.', ttlMs: 5000 }); }
    });
  }
}
