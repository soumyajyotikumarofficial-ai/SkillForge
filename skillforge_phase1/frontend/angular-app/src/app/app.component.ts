import { Component } from '@angular/core';
import { HttpClient, HttpEventType } from '@angular/common/http';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent {
  file: File | null = null;
  status = 'Not started';
  result: any = null;

  constructor(private http: HttpClient) {}

  onFileChange(ev: any) {
    this.file = ev.target.files[0] || null;
  }

  upload() {
    if (!this.file) return;
    this.status = 'Uploading...';
    const fd = new FormData();
    fd.append('file', this.file, this.file.name);
    this.http.post('/api/candidate/upload', fd, { reportProgress: true, observe: 'events' })
      .subscribe(evt => {
        if (evt.type === HttpEventType.UploadProgress) {
          const pct = Math.round(100 * (evt.loaded || 0) / (evt.total || 1));
          this.status = `Uploading ${pct}%`;
        } else if (evt.type === HttpEventType.Response) {
          this.result = evt.body;
          this.status = 'Upload complete — analysis below';
        }
      }, err => {
        this.status = 'Upload failed';
        console.error(err);
      });
  }
}
