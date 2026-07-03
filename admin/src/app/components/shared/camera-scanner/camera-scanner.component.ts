import { Component, ElementRef, EventEmitter, OnDestroy, Output, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Capacitor } from '@capacitor/core';
import { BarcodeScanner, BarcodeFormat } from '@capacitor-mlkit/barcode-scanning';

// Two independent scanning paths, picked automatically:
//  - Native app (Capacitor/Android): Google's ML Kit code-scanner module — a
//    full-screen native camera UI that's reliable across every Android device
//    and WebView version (unlike browser APIs, which vary wildly on WebView).
//  - Regular browser: the native BarcodeDetector API where available (Chrome/
//    Edge desktop + Android). Falls back to a manual-entry message on
//    browsers without it (desktop Safari/Firefox) — hardware scanners and
//    typing the code still work via the barcode input field.
@Component({
  selector: 'app-camera-scanner',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="scan-back" *ngIf="open" (click)="close()">
      <div class="scan-box" (click)="$event.stopPropagation()">
        <div class="scan-head">
          <span>📷 Scan Barcode</span>
          <button class="scan-x" (click)="close()">✕</button>
        </div>
        <video #video class="scan-video" autoplay playsinline muted></video>
        <p *ngIf="error" class="scan-err">{{ error }}</p>
        <p *ngIf="!error" class="scan-hint">Point the camera at a barcode.</p>
      </div>
    </div>
  `,
  styles: [`
    .scan-back { position: fixed; inset: 0; background: rgba(0,0,0,.7); z-index: 80; display: flex; align-items: center; justify-content: center; padding: 16px; }
    .scan-box { background: #12112a; border: 1px solid #2a2850; border-radius: 16px; padding: 16px; width: 100%; max-width: 420px; }
    .scan-head { display: flex; justify-content: space-between; align-items: center; color: #fff; font-weight: 700; margin-bottom: 10px; }
    .scan-x { background: #22203f; border: 0; color: #9ca3af; width: 28px; height: 28px; border-radius: 8px; cursor: pointer; }
    .scan-video { width: 100%; border-radius: 10px; background: #000; max-height: 320px; object-fit: cover; }
    .scan-hint, .scan-err { text-align: center; font-size: 12px; color: #9ca3af; margin-top: 10px; }
    .scan-err { color: #f87171; }
  `]
})
export class CameraScannerComponent implements OnDestroy {
  @ViewChild('video') videoRef?: ElementRef<HTMLVideoElement>;
  @Output() scanned = new EventEmitter<string>();

  open = false;
  error = '';
  private stream: MediaStream | null = null;
  private raf: number | null = null;

  private readonly nativeFormats = [
    BarcodeFormat.Code39, BarcodeFormat.Code128, BarcodeFormat.Ean13,
    BarcodeFormat.Ean8, BarcodeFormat.UpcA, BarcodeFormat.UpcE
  ];

  async show() {
    this.error = '';
    if (Capacitor.isNativePlatform()) {
      await this.scanNative();
      return;
    }
    this.open = true;
    await this.scanBrowser();
  }

  // ── Native (Capacitor app) ──────────────────────────────────────────────
  private async scanNative() {
    try {
      const { supported } = await BarcodeScanner.isSupported();
      if (!supported) {
        this.error = 'This device does not support barcode scanning. Use manual entry instead.';
        this.open = true;
        return;
      }
      if (!(await this.ensureNativePermission())) {
        this.error = 'Camera permission was denied. Enable it in app settings, or use manual entry.';
        this.open = true;
        return;
      }
      // Opens Google's full-screen native scanner UI — our own modal stays
      // closed so it doesn't flash behind it.
      const { barcodes } = await BarcodeScanner.scan({ formats: this.nativeFormats });
      if (barcodes && barcodes.length > 0) this.scanned.emit(barcodes[0].rawValue);
    } catch {
      this.error = 'Could not open the scanner. Use manual entry instead.';
      this.open = true;
    }
  }

  private async ensureNativePermission(): Promise<boolean> {
    const status = await BarcodeScanner.checkPermissions();
    if (status.camera === 'granted' || status.camera === 'limited') return true;
    if (status.camera === 'denied') return false;
    const req = await BarcodeScanner.requestPermissions();
    return req.camera === 'granted' || req.camera === 'limited';
  }

  // ── Browser (regular web) ───────────────────────────────────────────────
  private async scanBrowser() {
    const BD = (window as any).BarcodeDetector;
    if (!BD) {
      this.error = 'Camera scanning is not supported in this browser. Use manual entry or a hardware scanner instead.';
      return;
    }
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
      // Wait a tick for the *ngIf video element to exist in the DOM.
      setTimeout(() => {
        if (this.videoRef) {
          this.videoRef.nativeElement.srcObject = this.stream;
          this.detectLoop(new BD({ formats: ['code_39', 'code_128', 'ean_13', 'ean_8', 'upc_a', 'upc_e'] }));
        }
      });
    } catch {
      this.error = 'Could not access the camera. Check permissions and try again.';
    }
  }

  private detectLoop(detector: any) {
    const tick = async () => {
      if (!this.open || !this.videoRef) return;
      try {
        const codes = await detector.detect(this.videoRef.nativeElement);
        if (codes && codes.length > 0) {
          this.scanned.emit(codes[0].rawValue);
          this.close();
          return;
        }
      } catch { /* detection failure on a given frame is expected — keep trying */ }
      this.raf = requestAnimationFrame(tick);
    };
    this.raf = requestAnimationFrame(tick);
  }

  close() {
    this.open = false;
    if (this.raf) cancelAnimationFrame(this.raf);
    this.stream?.getTracks().forEach(t => t.stop());
    this.stream = null;
  }

  ngOnDestroy() { this.close(); }
}
