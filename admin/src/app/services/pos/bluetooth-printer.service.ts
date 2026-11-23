import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { Capacitor } from '@capacitor/core';
import { PAPER_WIDTH_DOTS, ReceiptData, renderReceipt } from '../../utils/receipt-canvas.util';
import { buildReceiptJob, chunk, toMonochrome } from '../../utils/escpos.util';

// GATT services that thermal printers commonly expose for raw printing. Web Bluetooth must be told up front
// which services it may touch, so the list matters.
const PRINTER_SERVICES = [
  '000018f0-0000-1000-8000-00805f9b34fb',
  '0000ff00-0000-1000-8000-00805f9b34fb',
  '0000ffe0-0000-1000-8000-00805f9b34fb',
  '0000fff0-0000-1000-8000-00805f9b34fb',
  '49535343-fe7d-4ae5-8fa9-9fafd205e455',
  'e7810a71-73ae-499d-8c15-faa9aef0c3f2'
];

const PREF_KEY = 'pos_bt_printer';

export type PrinterStatus = 'idle' | 'connecting' | 'ready' | 'printing' | 'error';

interface Transport {
  name: string;
  write(chunks: Uint8Array[]): Promise<void>;
  disconnect(): void;
}

/**
 * Prints receipts to a Bluetooth thermal printer.
 *  • In Chrome / Edge on Android and desktop: Web Bluetooth.
 *  • In the packaged app: the Capacitor BLE plugin (needs `npx cap sync`; see docs).
 * Only printers that speak Bluetooth LOW ENERGY work. Older "classic" (SPP) printers are not visible to either
 * API and need a different plugin. Nothing here has been run against a physical printer yet.
 */
@Injectable({ providedIn: 'root' })
export class BluetoothPrinterService {
  readonly status$ = new BehaviorSubject<PrinterStatus>('idle');
  readonly deviceName$ = new BehaviorSubject<string>('');
  readonly error$ = new BehaviorSubject<string>('');
  paperWidth: keyof typeof PAPER_WIDTH_DOTS = 'mm58';

  private transport: Transport | null = null;

  get native(): boolean { return Capacitor.isNativePlatform(); }
  get supported(): boolean { return this.native || (typeof navigator !== 'undefined' && !!(navigator as any).bluetooth); }

  async connect(): Promise<void> {
    if (this.status$.value === 'connecting') return;
    this.status$.next('connecting'); this.error$.next('');
    try {
      this.transport?.disconnect();
      this.transport = this.native ? await this.connectNative() : await this.connectWeb();
      this.deviceName$.next(this.transport.name);
      this.status$.next('ready');
    } catch (e: any) {
      this.transport = null;
      this.deviceName$.next('');
      // The user closing the device chooser is not an error worth showing.
      const cancelled = e?.name === 'NotFoundError' || /cancel/i.test(e?.message || '');
      this.status$.next(cancelled ? 'idle' : 'error');
      if (!cancelled) this.error$.next(friendly(e));
      throw e;
    }
  }

  async print(data: ReceiptData): Promise<void> {
    if (!this.transport) await this.connect();
    const t = this.transport!;
    this.status$.next('printing'); this.error$.next('');
    try {
      const img = renderReceipt(data, PAPER_WIDTH_DOTS[this.paperWidth]);
      const job = buildReceiptJob(toMonochrome(img.rgba, img.width, img.height));
      await t.write(chunk(job, 100));
      this.status$.next('ready');
    } catch (e: any) {
      // Most likely the printer was switched off or went out of range: force a fresh connect next time.
      this.transport?.disconnect(); this.transport = null;
      this.status$.next('error'); this.error$.next(friendly(e));
      throw e;
    }
  }

  disconnect() {
    this.transport?.disconnect(); this.transport = null;
    this.deviceName$.next(''); this.status$.next('idle');
  }

  // ── browser (Web Bluetooth) ───────────────────────────────────────────────
  private async connectWeb(): Promise<Transport> {
    const bt = (navigator as any).bluetooth;
    const device = await bt.requestDevice({ acceptAllDevices: true, optionalServices: PRINTER_SERVICES });
    const server = await device.gatt.connect();
    let characteristic: any = null;
    for (const service of await server.getPrimaryServices()) {
      for (const c of await service.getCharacteristics()) {
        if (c.properties.writeWithoutResponse || c.properties.write) { characteristic = c; break; }
      }
      if (characteristic) break;
    }
    if (!characteristic) { device.gatt.disconnect(); throw new Error('This device has no writable printing channel.'); }
    const noResponse = !!characteristic.properties.writeWithoutResponse;
    return {
      name: device.name || 'Bluetooth printer',
      disconnect: () => { try { device.gatt.disconnect(); } catch { /* already gone */ } },
      write: async (chunks) => {
        for (const c of chunks) {
          if (!device.gatt.connected) throw new Error('The printer disconnected.');
          if (noResponse && characteristic.writeValueWithoutResponse) await characteristic.writeValueWithoutResponse(c);
          else await characteristic.writeValue(c);
          await sleep(20);                                    // small printers drop data when fed too fast
        }
      }
    };
  }

  // ── packaged app (Capacitor BLE plugin) ───────────────────────────────────
  private async connectNative(): Promise<Transport> {
    const { BleClient } = await import('@capacitor-community/bluetooth-le');
    await BleClient.initialize({ androidNeverForLocation: true });
    const device = await BleClient.requestDevice({ optionalServices: PRINTER_SERVICES });
    await BleClient.connect(device.deviceId);
    let target: { service: string; characteristic: string; noResponse: boolean } | null = null;
    for (const s of await BleClient.getServices(device.deviceId)) {
      const c = s.characteristics.find(x => x.properties.writeWithoutResponse || x.properties.write);
      if (c) { target = { service: s.uuid, characteristic: c.uuid, noResponse: !!c.properties.writeWithoutResponse }; break; }
    }
    if (!target) { await BleClient.disconnect(device.deviceId); throw new Error('This device has no writable printing channel.'); }
    const t = target;
    try { localStorage.setItem(PREF_KEY, JSON.stringify({ id: device.deviceId, name: device.name })); } catch { /* ignore */ }
    return {
      name: device.name || 'Bluetooth printer',
      disconnect: () => { void BleClient.disconnect(device.deviceId).catch(() => { /* already gone */ }); },
      write: async (chunks) => {
        for (const c of chunks) {
          const view = new DataView(c.buffer, c.byteOffset, c.byteLength);
          if (t.noResponse) await BleClient.writeWithoutResponse(device.deviceId, t.service, t.characteristic, view);
          else await BleClient.write(device.deviceId, t.service, t.characteristic, view);
          await sleep(20);
        }
      }
    };
  }
}

const sleep = (ms: number) => new Promise(r => setTimeout(r, ms));

function friendly(e: any): string {
  const m = String(e?.message || e || '');
  if (/User cancelled|cancelled/i.test(m)) return 'No printer was chosen.';
  if (/GATT|disconnected|NetworkError/i.test(m)) return 'The printer disconnected. Switch it on, bring it close and try again.';
  if (/permission|denied|Security/i.test(m)) return 'Bluetooth permission was denied. Allow Bluetooth for this app and try again.';
  return m || 'Could not print.';
}
