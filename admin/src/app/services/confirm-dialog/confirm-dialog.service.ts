import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;  // red confirm button
}

interface ConfirmState {
  options: ConfirmOptions;
  resolve: (result: boolean) => void;
}

@Injectable({ providedIn: 'root' })
export class ConfirmDialogService {
  readonly state$ = new Subject<ConfirmState | null>();

  confirm(options: ConfirmOptions): Promise<boolean> {
    return new Promise(resolve => {
      this.state$.next({ options, resolve });
    });
  }
}
