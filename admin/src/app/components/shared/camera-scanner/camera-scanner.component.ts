import { Component, ElementRef, EventEmitter, OnDestroy, Output, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';

// Uses the browser-native BarcodeDetector API (Chrome/Edge desktop + Android,
// including mobile) — no external library, works fully offline once the page
// is loaded. Falls back to a manual-entry hint on browsers without support
// (notably desktop Safari/Firefox).
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

  async show() {
    this.open = true;
    this.error = '';
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
