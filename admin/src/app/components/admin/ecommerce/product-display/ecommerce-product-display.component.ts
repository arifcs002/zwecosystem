import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, CompanySetting } from '../../../../services/settings/settings.service';
import { GlobalNotificationService } from '../../../../services/global-notification/global-notification.service';

@Component({
  selector: 'app-ecommerce-product-display',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './ecommerce-product-display.component.html',
  styleUrl: '../ecommerce-config.css'
})
export class EcommerceProductDisplayComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private notify = inject(GlobalNotificationService);

  lowStockThreshold = 5;
  newBadgeDays = 14;
  enableWhatsapp = false;
  whatsappNumber = '';
  enableCall = false;
  callNumber = '';

  isLoading = false;
  saving = false;

  ngOnInit() {
    this.isLoading = true;
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        settings.forEach(s => {
          if (s.key === 'low_stock_threshold' && s.value) this.lowStockThreshold = +s.value;
          if (s.key === 'new_badge_days' && s.value) this.newBadgeDays = +s.value;
          if (s.key === 'enable_whatsapp_order') this.enableWhatsapp = s.value === 'true';
          if (s.key === 'whatsapp_number') this.whatsappNumber = s.value;
          if (s.key === 'enable_call_order') this.enableCall = s.value === 'true';
          if (s.key === 'call_number') this.callNumber = s.value;
        });
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  save() {
    this.saving = true;
    const settings: CompanySetting[] = [
      { key: 'low_stock_threshold', value: String(this.lowStockThreshold || 5), groupName: 'STOREFRONT' },
      { key: 'new_badge_days', value: String(this.newBadgeDays || 0), groupName: 'STOREFRONT' },
      { key: 'enable_whatsapp_order', value: this.enableWhatsapp ? 'true' : 'false', groupName: 'STOREFRONT' },
      { key: 'whatsapp_number', value: this.whatsappNumber, groupName: 'STOREFRONT' },
      { key: 'enable_call_order', value: this.enableCall ? 'true' : 'false', groupName: 'STOREFRONT' },
      { key: 'call_number', value: this.callNumber, groupName: 'STOREFRONT' }
    ];
    this.settingsService.updateSettings(settings).subscribe({
      next: () => { this.saving = false; this.notify.notify({ type: 'success', title: 'Saved', message: 'Product display settings saved.', ttlMs: 3000 }); },
      error: () => { this.saving = false; this.notify.notify({ type: 'error', title: 'Failed', message: 'Could not save.', ttlMs: 5000 }); }
    });
  }
}
