import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ConfirmDialogService, ConfirmOptions } from '../../../services/confirm-dialog/confirm-dialog.service';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div *ngIf="visible"
      class="fixed inset-0 z-[9999] flex items-center justify-center p-4"
      (click)="onBackdrop($event)">

      <!-- Backdrop -->
      <div class="absolute inset-0 bg-black/70 backdrop-blur-sm"></div>

      <!-- Modal -->
      <div class="relative bg-[#12112a] border border-white/10 rounded-2xl shadow-2xl w-full max-w-sm
                  animate-in fade-in zoom-in-95 duration-150">

        <!-- Icon + Title -->
        <div class="p-6 pb-3">
          <div class="flex items-start gap-4">
            <div class="flex-shrink-0 w-10 h-10 rounded-full flex items-center justify-center text-lg"
              [ngClass]="options?.danger
                ? 'bg-rose-500/15 border border-rose-500/30'
                : 'bg-violet-500/15 border border-violet-500/30'">
              {{ options?.danger ? '⚠️' : '❓' }}
            </div>
            <div>
              <h3 class="text-base font-semibold text-gray-100">{{ options?.title }}</h3>
              <p class="text-sm text-gray-400 mt-1 leading-relaxed">{{ options?.message }}</p>
            </div>
          </div>
        </div>

        <!-- Divider -->
        <div class="border-t border-white/[0.06] mx-6"></div>

        <!-- Buttons -->
        <div class="flex justify-end gap-3 p-5">
          <button (click)="cancel()"
            class="px-4 py-2 text-sm rounded-lg bg-white/5 hover:bg-white/10
                   border border-white/10 text-gray-300 transition-colors font-medium">
            {{ options?.cancelLabel || 'Cancel' }}
          </button>
          <button (click)="confirm()"
            class="px-5 py-2 text-sm rounded-lg font-medium transition-colors"
            [ngClass]="options?.danger
              ? 'bg-rose-600 hover:bg-rose-700 text-white'
              : 'bg-violet-600 hover:bg-violet-700 text-white'">
            {{ options?.confirmLabel || 'Confirm' }}
          </button>
        </div>
      </div>
    </div>
  `
})
export class ConfirmDialogComponent implements OnInit, OnDestroy {
  private svc = inject(ConfirmDialogService);
  private sub!: Subscription;
  private resolveFn: ((v: boolean) => void) | null = null;

  visible = false;
  options: ConfirmOptions | null = null;

  ngOnInit() {
    this.sub = this.svc.state$.subscribe(state => {
      if (state) {
        this.options = state.options;
        this.resolveFn = state.resolve;
        this.visible = true;
      } else {
        this.visible = false;
        this.options = null;
        this.resolveFn = null;
      }
    });
  }

  ngOnDestroy() { this.sub.unsubscribe(); }

  confirm() { this.resolveFn?.(true);  this.close(); }
  cancel()  { this.resolveFn?.(false); this.close(); }

  onBackdrop(e: MouseEvent) {
    if ((e.target as HTMLElement).classList.contains('fixed')) this.cancel();
  }

  private close() { this.svc.state$.next(null); }
}
