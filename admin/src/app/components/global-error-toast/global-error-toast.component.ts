import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { GlobalNotificationService, NotificationType } from '../../services/global-notification/global-notification.service';

@Component({
  selector: 'app-global-error-toast',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './global-error-toast.component.html',
  styleUrl: './global-error-toast.component.css'
})
export class GlobalErrorToastComponent {
  private notificationService = inject(GlobalNotificationService);
  notifications$ = this.notificationService.notifications$;

  dismiss(id: number) {
    this.notificationService.dismiss(id);
  }

  icon(type: NotificationType): string {
    switch (type) {
      case 'success': return '✓';
      case 'warning': return '!';
      case 'info': return 'i';
      default: return '!';
    }
  }

  tone(type: NotificationType): string {
    switch (type) {
      case 'success': return 'success';
      case 'warning': return 'warning';
      case 'info': return 'info';
      default: return 'error';
    }
  }
}
