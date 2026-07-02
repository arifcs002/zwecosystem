import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, CompanySetting } from '../../../../services/settings/settings.service';
import { GlobalNotificationService } from '../../../../services/global-notification/global-notification.service';
import { buildProductIdPreview, toSecretCode, DEFAULT_SECRET_MAP } from '../../../../utils/secret-code.util';

@Component({
  selector: 'app-product-codes',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './product-codes.component.html',
  styleUrl: '../ecommerce-config.css'
})
export class ProductCodesComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private notify = inject(GlobalNotificationService);

  prefix = 'P';
  format = '{PREFIX}{YY}{MM}-{SEQ}';
  pad = 4;

  // Secret map: one character per digit index 0..9.
  secretChars: string[] = DEFAULT_SECRET_MAP.split('');
  samplePrice = 1499;

  readonly tokens = ['{PREFIX}', '{SEQ}', '{YYYY}', '{YY}', '{MM}', '{DD}', '{HH}', '{mm}', '{ss}'];
  isLoading = false;
  saving = false;

  ngOnInit() {
    this.isLoading = true;
    this.settingsService.getSettings().subscribe({
      next: (s) => {
        const v = (k: string) => s.find(x => x.key === k)?.value;
        this.prefix = v('product_id_prefix') ?? 'P';
        this.format = v('product_id_format') || '{PREFIX}{YY}{MM}-{SEQ}';
        this.pad = +(v('product_id_seq_pad') || 4) || 4;
        const map = v('secret_price_map');
        if (map && map.length >= 10) this.secretChars = map.slice(0, 10).split('');
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  get idPreview(): string { return buildProductIdPreview(this.format, this.prefix, 1, this.pad); }
  get secretMap(): string { return this.secretChars.join(''); }
  get secretPreview(): string { return toSecretCode(this.samplePrice, this.secretMap); }

  addToken(t: string) { this.format = (this.format || '') + t; }

  save() {
    // Basic guard: unique characters recommended but not enforced.
    this.saving = true;
    const settings: CompanySetting[] = [
      { key: 'product_id_prefix', value: this.prefix, groupName: 'PRODUCT_ID' },
      { key: 'product_id_format', value: this.format, groupName: 'PRODUCT_ID' },
      { key: 'product_id_seq_pad', value: String(this.pad || 4), groupName: 'PRODUCT_ID' },
      { key: 'secret_price_map', value: this.secretMap, groupName: 'SECRET' }
    ];
    this.settingsService.updateSettings(settings).subscribe({
      next: () => { this.saving = false; this.notify.notify({ type: 'success', title: 'Saved', message: 'Product ID & secret code settings saved.', ttlMs: 3000 }); },
      error: () => { this.saving = false; this.notify.notify({ type: 'error', title: 'Failed', message: 'Could not save.', ttlMs: 5000 }); }
    });
  }
}
