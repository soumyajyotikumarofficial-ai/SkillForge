import { Component } from '@angular/core';
import { HttpClient } from '@angular/common/http';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent {
  file: File | null = null;
  dragActive = false;
  status = 'Drop a PDF, DOCX or TXT resume here';
  loading = false;
  analysis: any = null;

  constructor(private http: HttpClient) {}

  onFileChange(event: Event) {
    const input = event.target as HTMLInputElement;
    if (input?.files?.length) {
      this.setFile(input.files[0]);
    }
  }

  onDragOver(event: DragEvent) {
    event.preventDefault();
    this.dragActive = true;
  }

  onDragLeave(event: DragEvent) {
    event.preventDefault();
    this.dragActive = false;
  }

  onDrop(event: DragEvent) {
    event.preventDefault();
    this.dragActive = false;
    const file = event.dataTransfer?.files?.[0];
    if (file) {
      this.setFile(file);
    }
  }

  private setFile(file: File) {
    this.file = file;
    this.status = file.name;
    this.analysis = null;
  }

  upload() {
    if (!this.file) {
      this.status = 'Please select a file first';
      return;
    }

    this.loading = true;
    this.status = 'Analyzing resume...';
    const payload = new FormData();
    payload.append('file', this.file, this.file.name);

    this.http.post('/api/candidate/upload', payload).subscribe({
      next: (result: any) => {
        this.analysis = result;
        this.status = 'Resume analyzed successfully';
        this.loading = false;
      },
      error: error => {
        this.status = 'Upload failed. Please try again.';
        console.error(error);
        this.loading = false;
      }
    });
  }
}
