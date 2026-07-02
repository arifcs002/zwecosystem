import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, CompanySetting } from '../../../../services/settings/settings.service';
import { GlobalNotificationService } from '../../../../services/global-notification/global-notification.service';

@Component({
  selector: 'app-ecommerce-pages',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './ecommerce-pages.component.html',
  styleUrl: '../ecommerce-config.css'
})
export class EcommercePagesComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private notify = inject(GlobalNotificationService);

  pageAbout = '';
  pagePrivacy = '';
  pageRefund = '';
  pageTerms = '';
  contactAddress = '';
  contactPhone = '';
  contactEmail = '';

  isLoading = false;
  saving = false;

  ngOnInit() {
    this.isLoading = true;
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        const v = (k: string) => settings.find(s => s.key === k)?.value || '';
        this.pageAbout = v('page_about');
        this.pagePrivacy = v('page_privacy');
        this.pageRefund = v('page_refund');
        this.pageTerms = v('page_terms');
        this.contactAddress = v('contact_address');
        this.contactPhone = v('contact_phone');
        this.contactEmail = v('contact_email');
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  save() {
    this.saving = true;
    const settings: CompanySetting[] = [
      { key: 'page_about', value: this.pageAbout, groupName: 'PAGES' },
      { key: 'page_privacy', value: this.pagePrivacy, groupName: 'PAGES' },
      { key: 'page_refund', value: this.pageRefund, groupName: 'PAGES' },
      { key: 'page_terms', value: this.pageTerms, groupName: 'PAGES' },
      { key: 'contact_address', value: this.contactAddress, groupName: 'PAGES' },
      { key: 'contact_phone', value: this.contactPhone, groupName: 'PAGES' },
      { key: 'contact_email', value: this.contactEmail, groupName: 'PAGES' }
    ];
    this.settingsService.updateSettings(settings).subscribe({
      next: () => { this.saving = false; this.notify.notify({ type: 'success', title: 'Saved', message: 'Pages & policies saved.', ttlMs: 3000 }); },
      error: () => { this.saving = false; this.notify.notify({ type: 'error', title: 'Failed', message: 'Could not save.', ttlMs: 5000 }); }
    });
  }
}
