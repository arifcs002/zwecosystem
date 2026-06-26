import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

export type NotificationType = 'error' | 'warning' | 'success' | 'info';

export interface GlobalNotification {
  id: number;
  type: NotificationType;
  title: string;
  message: string;
  detail?: string;
  ttlMs?: number;
}

@Injectable({
  providedIn: 'root'
})
export class GlobalNotificationService {
  private readonly notificationsSubject = new BehaviorSubject<GlobalNotification[]>([]);
  readonly notifications$ = this.notificationsSubject.asObservable();
  private nextId = 1;

  notify(notification: Omit<GlobalNotification, 'id'>) {
    const current = this.notificationsSubject.value;
    const item: GlobalNotification = { id: this.nextId++, ...notification };
    this.notificationsSubject.next([item, ...current].slice(0, 4));

    const ttl = notification.ttlMs ?? 5500;
    if (ttl > 0) {
      window.setTimeout(() => this.dismiss(item.id), ttl);
    }
  }

  dismiss(id: number) {
    this.notificationsSubject.next(this.notificationsSubject.value.filter(item => item.id !== id));
  }

  clear() {
    this.notificationsSubject.next([]);
  }
}
