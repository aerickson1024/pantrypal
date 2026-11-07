import { Component, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { OnInit, inject } from '@angular/core';

@Component({
  selector: 'app-root',
  template: `
    <main>
      <h1>PantryPal</h1>
      <p>Backend: <span aria-live="polite">{{ apiStatus() }}</span></p>
    </main>
  `,
})
export class App implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly apiStatus = signal('Checking...');

  ngOnInit(): void {
    this.http.get<{ status: string }>('http://localhost:5044/api/health').subscribe({
      next: (response) => this.apiStatus.set(response.status === 'ok' ? 'Connected' : 'Unexpected response'),
      error: () => this.apiStatus.set('Unavailable'),
    });
  }
}
