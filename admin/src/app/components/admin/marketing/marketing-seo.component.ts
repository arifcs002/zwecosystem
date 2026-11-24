import { Component, OnInit, inject } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { CompanySetting, SettingsService } from '../../../services/settings/settings.service';
import { resolveImageUrl } from '../../../utils/image-url.util';

const PIXEL_ID = /^\d{5,20}$/;
const GA4_ID = /^G-[A-Z0-9]{6,14}$/;
const KEYS = ['meta_pixel_id', 'ga4_measurement_id', 'secret_meta_capi_token', 'meta_test_event_code', 'seo_title', 'seo_description', 'seo_og_image'];

@Component({
    selector: 'app-marketing-seo',
    imports: [FormsModule],
    template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">📣 Marketing &amp; SEO</h1>
      <p class="page-subtitle">Track your Facebook ads, and control how your shop looks on Google and when a link is shared</p>
    </div>
    <button class="btn-primary" (click)="save()" [disabled]="saving || loading">{{ saving ? 'Saving…' : 'Save all' }}</button>
  </div>

  @if (successMsg) {
    <div class="alert-success">✅ {{ successMsg }}</div>
  }
  @if (errorMsg) {
    <div class="alert-error">❌ {{ errorMsg }}</div>
  }
  @if (loading) {
    <div class="loading">Loading…</div>
  }

  @if (!loading) {
    <div class="table-card" style="padding:18px;margin-bottom:16px">
      <h3 style="margin:0 0 4px">Facebook (Meta) Pixel</h3>
      <p style="opacity:.65;font-size:13px;margin:0 0 12px">Lets Facebook see who views, adds to cart and buys, so your ads find more buyers. Find the ID in Meta Events Manager → Data sources → your Pixel.</p>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px">
        <div class="form-group"><label>Pixel ID</label>
        <input class="form-input" [(ngModel)]="v['meta_pixel_id']" inputmode="numeric" placeholder="e.g. 123456789012345" maxlength="20" />
        @if (v['meta_pixel_id'] && !pixelOk) {
          <small style="color:#f87171">Pixel IDs are 5-20 digits.</small>
        }</div>
        <div class="form-group"><label>Conversions API access token <span style="opacity:.6">(optional, recommended)</span></label>
        <input class="form-input" type="password" autocomplete="off" [(ngModel)]="v['secret_meta_capi_token']" />
        <small style="opacity:.6">Sends each sale from our server too, so ad blockers and iOS don't hide it. Stored write-only.</small></div>
        <div class="form-group"><label>Test event code <span style="opacity:.6">(only while testing)</span></label>
        <input class="form-input" [(ngModel)]="v['meta_test_event_code']" placeholder="e.g. TEST12345" maxlength="40" />
        <small style="opacity:.6">From Events Manager → Test events. Clear it once you've checked a sale appears.</small></div>
      </div>
    </div>
    <div class="table-card" style="padding:18px;margin-bottom:16px">
      <h3 style="margin:0 0 4px">Google Analytics 4</h3>
      <div class="form-group" style="max-width:340px"><label>Measurement ID</label>
      <input class="form-input" [(ngModel)]="v['ga4_measurement_id']" placeholder="G-XXXXXXXXXX" maxlength="16" style="text-transform:uppercase" />
      @if (v['ga4_measurement_id'] && !gaOk) {
        <small style="color:#f87171">It looks like G- followed by letters/numbers.</small>
      }</div>
    </div>
    <div class="table-card" style="padding:18px">
      <h3 style="margin:0 0 4px">Search &amp; link previews</h3>
      <p style="opacity:.65;font-size:13px;margin:0 0 12px">This is what shows on Google and in the card when someone shares your shop link on Facebook or WhatsApp. Each product also gets its own preview automatically (name, price, photo).</p>
      <div style="display:grid;grid-template-columns:1.2fr 1fr;gap:18px">
        <div>
          <div class="form-group"><label>Shop title</label><input class="form-input" [(ngModel)]="v['seo_title']" maxlength="70" placeholder="e.g. Zaira's World — Shoes in Dhaka" />
          <small style="opacity:.6">{{ (v['seo_title'] || '').length }}/70 — keep it under about 60 characters.</small></div>
          <div class="form-group"><label>Description</label><textarea class="form-input" rows="3" [(ngModel)]="v['seo_description']" maxlength="300" placeholder="One or two sentences about what you sell"></textarea>
          <small style="opacity:.6">{{ (v['seo_description'] || '').length }}/300 — about 150 characters shows fully on Google.</small></div>
          <div class="form-group"><label>Share image (URL)</label><input class="form-input" [(ngModel)]="v['seo_og_image']" placeholder="/uploads/... or https://..." />
          <small style="opacity:.6">Best size 1200×630. Empty = your shop logo.</small></div>
        </div>
        <div>
          <div style="opacity:.65;font-size:12px;margin-bottom:6px">Preview of a shared link</div>
          <div style="border:1px solid rgba(255,255,255,.12);border-radius:12px;overflow:hidden;background:#fff;color:#111">
            <div style="height:130px;background:#e5e7eb center/cover no-repeat" [style.background-image]="previewImage ? 'url(' + previewImage + ')' : 'none'"></div>
            <div style="padding:10px 12px">
              <div style="font-size:11px;color:#6b7280;text-transform:uppercase">your-shop.auleco.com</div>
              <div style="font-weight:700;margin:2px 0">{{ v['seo_title'] || 'Your shop title' }}</div>
              <div style="font-size:12.5px;color:#4b5563">{{ v['seo_description'] || 'Your description appears here.' }}</div>
            </div>
          </div>
        </div>
      </div>
      <p style="opacity:.6;font-size:12.5px;margin:12px 0 0">Your sitemap is at <code>/sitemap.xml</code> and robots at <code>/robots.txt</code> on your shop address — submit the sitemap in Google Search Console. To check a link preview, paste a product link into Facebook's Sharing Debugger.</p>
    </div>
  }
</div>`
})
export class MarketingSeoComponent implements OnInit {
  private settings = inject(SettingsService);

  v: Record<string, string> = {};
  loading = true; saving = false;
  successMsg = ''; errorMsg = '';

  get pixelOk() { return PIXEL_ID.test((this.v['meta_pixel_id'] || '').trim()); }
  get gaOk() { return GA4_ID.test((this.v['ga4_measurement_id'] || '').trim().toUpperCase()); }
  get previewImage() { const u = (this.v['seo_og_image'] || '').trim(); return u ? resolveImageUrl(u) : ''; }

  ngOnInit() {
    this.settings.getSettings().subscribe({
      next: (all) => { for (const k of KEYS) this.v[k] = all.find(s => s.key === k)?.value ?? ''; this.loading = false; },
      error: () => { this.loading = false; this.errorMsg = 'Could not load settings.'; }
    });
  }

  save() {
    this.errorMsg = '';
    const pixel = (this.v['meta_pixel_id'] || '').trim();
    const ga = (this.v['ga4_measurement_id'] || '').trim().toUpperCase();
    if (pixel && !PIXEL_ID.test(pixel)) { this.errorMsg = 'The Pixel ID should be 5-20 digits.'; return; }
    if (ga && !GA4_ID.test(ga)) { this.errorMsg = 'The Google Analytics ID should look like G-XXXXXXXXXX.'; return; }
    this.saving = true;
    this.v['ga4_measurement_id'] = ga;
    const payload: CompanySetting[] = KEYS.map(k => ({
      key: k, value: (this.v[k] ?? '').trim(),
      groupName: k.startsWith('secret_') ? 'MARKETING' : k.startsWith('seo_') ? 'SEO' : 'MARKETING'
    }));
    this.settings.updateSettings(payload).subscribe({
      next: () => { this.saving = false; this.successMsg = 'Saved.'; setTimeout(() => this.successMsg = '', 3000); },
      error: (e) => { this.saving = false; this.errorMsg = e?.status === 403 ? 'Only the shop owner can change the access token.' : 'Could not save.'; }
    });
  }
}
