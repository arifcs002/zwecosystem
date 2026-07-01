import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AppVersionService, AppVersion, ServerApkFile } from '../../../services/app-version/app-version.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';
import { resolveImageUrl } from '../../../utils/image-url.util';

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
  serverFiles: ServerApkFile[] = [];
  isLoading = false;
  uploading = false;
  resolveUrl = resolveImageUrl;

  // Upload mode: 'browser' | 'server'
  uploadMode: 'browser' | 'server' = 'browser';

  apkFile: File | null = null;
  selectedServerFile = '';
  versionName = '';
  versionCode: number | null = null;
  releaseNotes = '';

  // Edit modal
  editMode = false;
  editId: number | null = null;
  editVersionName = '';
  editVersionCode: number | null = null;
  editReleaseNotes = '';
  editSaving = false;

  // Delete confirm
  deleteTargetId: number | null = null;
  deleteWithFile = false;
  deleteInProgress = false;

  ngOnInit() {
    this.load();
    this.loadServerFiles();
  }

  load() {
    this.isLoading = true;
    this.appVersionService.getAll().subscribe({
      next: (data) => { this.versions = data; this.isLoading = false; },
      error: () => { this.isLoading = false; }
    });
  }

  loadServerFiles() {
    this.appVersionService.getServerFiles().subscribe({
      next: (files) => { this.serverFiles = files; },
      error: () => {}
    });
  }

  onFileSelected(event: any) {
    const file = event.target.files?.[0];
    if (file) this.apkFile = file;
  }

  publish() {
    if (!this.versionName.trim()) {
      this.notify.notify({ type: 'warning', title: 'Missing version name', message: 'e.g. 1.0.0', ttlMs: 3000 });
      return;
    }
    if (!this.versionCode) {
      this.notify.notify({ type: 'warning', title: 'Missing version code', message: 'Must be higher than previous release.', ttlMs: 3000 });
      return;
    }

    if (this.uploadMode === 'browser') {
      if (!this.apkFile) {
        this.notify.notify({ type: 'warning', title: 'No file', message: 'Choose an .apk file first.', ttlMs: 3000 });
        return;
      }
      this.uploading = true;
      this.appVersionService.upload(this.apkFile, this.versionName, this.versionCode, this.releaseNotes).subscribe({
        next: (v) => this.onPublished(v),
        error: (err) => this.onPublishError(err)
      });
    } else {
      if (!this.selectedServerFile) {
        this.notify.notify({ type: 'warning', title: 'No file selected', message: 'Choose a server-side APK file.', ttlMs: 3000 });
        return;
      }
      this.uploading = true;
      this.appVersionService.registerExisting(this.selectedServerFile, this.versionName, this.versionCode, this.releaseNotes).subscribe({
        next: (v) => this.onPublished(v),
        error: (err) => this.onPublishError(err)
      });
    }
  }

  private onPublished(v: AppVersion) {
    this.uploading = false;
    this.notify.notify({ type: 'success', title: 'Published', message: `Version ${v.versionName} is now the active release.`, ttlMs: 4000 });
    this.apkFile = null;
    this.selectedServerFile = '';
    this.versionName = '';
    this.versionCode = null;
    this.releaseNotes = '';
    this.load();
    this.loadServerFiles();
  }

  private onPublishError(err: any) {
    this.uploading = false;
    this.notify.notify({ type: 'error', title: 'Upload failed', message: err.error?.message || 'Could not publish release.', ttlMs: 6000 });
  }

  // ── Edit ──────────────────────────────────────────────────────────────────
  openEdit(v: AppVersion) {
    this.editId = v.id;
    this.editVersionName = v.versionName;
    this.editVersionCode = v.versionCode;
    this.editReleaseNotes = v.releaseNotes || '';
    this.editMode = true;
  }

  saveEdit() {
    if (!this.editId || !this.editVersionName.trim() || !this.editVersionCode) return;
    this.editSaving = true;
    this.appVersionService.update(this.editId, this.editVersionName, this.editVersionCode, this.editReleaseNotes).subscribe({
      next: () => {
        this.editSaving = false;
        this.editMode = false;
        this.notify.notify({ type: 'success', title: 'Updated', message: 'Release info saved.', ttlMs: 3000 });
        this.load();
      },
      error: () => {
        this.editSaving = false;
        this.notify.notify({ type: 'error', title: 'Save failed', message: 'Could not update release.', ttlMs: 4000 });
      }
    });
  }

  // ── Delete ────────────────────────────────────────────────────────────────
  confirmDelete(id: number) {
    this.deleteTargetId = id;
    this.deleteWithFile = false;
  }

  doDelete() {
    if (!this.deleteTargetId) return;
    this.deleteInProgress = true;
    this.appVersionService.delete(this.deleteTargetId, this.deleteWithFile).subscribe({
      next: () => {
        this.deleteInProgress = false;
        this.deleteTargetId = null;
        this.notify.notify({ type: 'success', title: 'Deleted', message: 'Release removed.', ttlMs: 3000 });
        this.load();
        this.loadServerFiles();
      },
      error: () => {
        this.deleteInProgress = false;
        this.notify.notify({ type: 'error', title: 'Delete failed', message: 'Could not remove release.', ttlMs: 4000 });
      }
    });
  }
}
