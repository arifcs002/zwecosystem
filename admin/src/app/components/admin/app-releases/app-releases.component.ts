import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AppVersionService, AppVersion } from '../../../services/app-version/app-version.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';

@Component({
  selector: 'app-app-releases',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app-releases.component.html',
  styleUrl: './app-releases.component.css'
})
export class AppReleasesComponent implements OnInit {
  private appVersionService = inject(AppVersionService);
  private notify = inject(GlobalNotificationService);

  versions: AppVersion[] = [];
  isLoading = false;
  uploading = false;

  apkFile: File | null = null;
  versionName = '';
  versionCode: number | null = null;
  releaseNotes = '';

  ngOnInit() { this.load(); }

  load() {
    this.isLoading = true;
    this.appVersionService.getAll().subscribe({
      next: (data) => { this.versions = data; this.isLoading = false; },
      error: () => { this.isLoading = false; }
    });
  }

  onFileSelected(event: any) {
    const file = event.target.files?.[0];
    if (file) this.apkFile = file;
  }

  upload() {
    if (!this.apkFile) { this.notify.notify({ type: 'warning', title: 'No file', message: 'Choose an .apk file first.', ttlMs: 4000 }); return; }
    if (!this.versionName.trim()) { this.notify.notify({ type: 'warning', title: 'Missing version name', message: 'e.g. 1.0.0', ttlMs: 4000 }); return; }
    if (!this.versionCode) { this.notify.notify({ type: 'warning', title: 'Missing version code', message: 'A higher integer than the previous release.', ttlMs: 4000 }); return; }

    this.uploading = true;
    this.appVersionService.upload(this.apkFile, this.versionName, this.versionCode, this.releaseNotes).subscribe({
      next: () => {
        this.uploading = false;
        this.notify.notify({ type: 'success', title: 'Published', message: `Version ${this.versionName} is now the active release.`, ttlMs: 4000 });
        this.apkFile = null;
        this.versionName = '';
        this.versionCode = null;
        this.releaseNotes = '';
        this.load();
      },
      error: (err) => {
        this.uploading = false;
        this.notify.notify({ type: 'error', title: 'Upload failed', message: err.error?.message || 'Could not publish release.', ttlMs: 6000 });
      }
    });
  }
}
