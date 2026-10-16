import { Component, effect, inject, input, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { TranslateService } from '@ngx-translate/core';
import { Api } from '../../core/api';
import { ContentPage } from '../../core/models';

@Component({
  imports: [MatCardModule],
  template: `@if (page(); as p) { <div class="page page-narrow"><h1>{{ p.title }}</h1><mat-card appearance="outlined"><mat-card-content><div class="rich" [innerHTML]="p.content"></div></mat-card-content></mat-card></div> }`,
})
export class ContentPageView {
  private api = inject(Api); private translate = inject(TranslateService);
  slug = input.required<string>(); page = signal<ContentPage | null>(null);
  constructor() { effect(() => this.api.get<ContentPage>(`/api/pages/${this.slug()}`, { lang: this.translate.getCurrentLang() ?? 'de' }).subscribe(p => this.page.set(p))); }
}
