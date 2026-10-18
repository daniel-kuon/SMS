import { afterNextRender, Component, effect, ElementRef, inject, input, model, OnDestroy, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Image from '@tiptap/extension-image';
import { Placeholder } from '@tiptap/extension-placeholder';
import { Api, errorMessage } from '../core/api';

/** What the edited content belongs to: decides which uploads the image picker offers and where new uploads go. */
export interface RichContext { shipId?: string | null; tripId?: string | null; logbookEntryId?: string | null; postId?: string | null; visibility?: string | null; }
interface MediaPick { id: string; thumbUrl: string; url: string; fileName: string; caption: string | null; }
interface LinkTarget { type: string; id: string; title: string; subtitle: string | null; url: string; }

/** WYSIWYG editor (TipTap) for all sanitised rich-text fields; [(value)] is the HTML. */
@Component({
  selector: 'sms-rich-editor',
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    @if (label()) { <div class="label">{{ label() }}</div> }
    <div class="box">
      <div class="toolbar" role="toolbar">
        @for (b of buttons; track b.icon) {
          @if (b.sep) { <span class="sep"></span> }
          <button mat-icon-button type="button" [class.on]="active(b.name, b.attrs)" [disabled]="b.can && !can(b.can)" [title]="'editor.' + b.label | translate" [attr.aria-label]="'editor.' + b.label | translate"
            (mousedown)="$event.preventDefault()" (click)="b.run()"><mat-icon>{{ b.icon }}</mat-icon></button>
        }
      </div>
      <div #host class="content rich"></div>
    </div>`,
  styles: `
    :host { display: block; }
    .label { font-size: 12px; color: #666; margin: 0 0 4px 4px; }
    .box { border: 1px solid #bbb; border-radius: 4px; background: #fff; }
    .box:focus-within { border-color: var(--mat-sys-primary, #1565c0); }
    .toolbar { display: flex; flex-wrap: wrap; align-items: center; border-bottom: 1px solid #ddd; padding: 2px; position: sticky; top: 0; background: #fff; z-index: 1; }
    .toolbar button { --mat-icon-button-state-layer-size: 34px; width: 34px; height: 34px; padding: 5px; }
    .toolbar button.on { background: #e3eef9; color: #1b4d7a; border-radius: 50%; }
    .sep { width: 1px; height: 22px; background: #ddd; margin: 0 4px; }
    .content { padding: 8px 12px; min-height: 160px; }
    .content ::ng-deep .tiptap { outline: none; min-height: 140px; }
    .content ::ng-deep .tiptap p.is-editor-empty:first-child::before { content: attr(data-placeholder); color: #999; float: left; height: 0; pointer-events: none; }
    .content ::ng-deep .tiptap img.ProseMirror-selectednode { outline: 2px solid #39f; }`,
})
export class RichEditor implements OnDestroy {
  value = model('');
  context = input<RichContext>({});
  label = input('');
  placeholder = input('');
  private host = viewChild.required<ElementRef<HTMLElement>>('host');
  private dialog = inject(MatDialog);
  private translate = inject(TranslateService);
  editor?: Editor;
  /** Bumped on every editor transaction so the toolbar state re-renders. */
  private tick = signal(0);

  buttons: { icon: string; label: string; run: () => void; name?: string; attrs?: Record<string, unknown>; can?: 'undo' | 'redo'; sep?: boolean }[] = [
    { icon: 'notes', label: 'paragraph', name: 'paragraph', run: () => this.chain().setParagraph().run() },
    { icon: 'format_h2', label: 'heading2', name: 'heading', attrs: { level: 2 }, run: () => this.chain().toggleHeading({ level: 2 }).run() },
    { icon: 'format_h3', label: 'heading3', name: 'heading', attrs: { level: 3 }, run: () => this.chain().toggleHeading({ level: 3 }).run() },
    { icon: 'format_bold', label: 'bold', name: 'bold', sep: true, run: () => this.chain().toggleBold().run() },
    { icon: 'format_italic', label: 'italic', name: 'italic', run: () => this.chain().toggleItalic().run() },
    { icon: 'format_underlined', label: 'underline', name: 'underline', run: () => this.chain().toggleUnderline().run() },
    { icon: 'format_strikethrough', label: 'strike', name: 'strike', run: () => this.chain().toggleStrike().run() },
    { icon: 'format_list_bulleted', label: 'bulletList', name: 'bulletList', sep: true, run: () => this.chain().toggleBulletList().run() },
    { icon: 'format_list_numbered', label: 'orderedList', name: 'orderedList', run: () => this.chain().toggleOrderedList().run() },
    { icon: 'format_quote', label: 'blockquote', name: 'blockquote', run: () => this.chain().toggleBlockquote().run() },
    { icon: 'code_blocks', label: 'codeBlock', name: 'codeBlock', run: () => this.chain().toggleCodeBlock().run() },
    { icon: 'horizontal_rule', label: 'hr', run: () => this.chain().setHorizontalRule().run() },
    { icon: 'link', label: 'link', name: 'link', sep: true, run: () => this.openLink() },
    { icon: 'link_off', label: 'unlink', run: () => this.chain().extendMarkRange('link').unsetLink().run() },
    { icon: 'image', label: 'image', run: () => this.openImage() },
    { icon: 'undo', label: 'undo', can: 'undo', sep: true, run: () => this.chain().undo().run() },
    { icon: 'redo', label: 'redo', can: 'redo', run: () => this.chain().redo().run() },
  ];

  constructor() {
    afterNextRender(() => {
      this.editor = new Editor({
        element: this.host().nativeElement,
        extensions: [
          StarterKit.configure({ heading: { levels: [2, 3] }, link: { openOnClick: false, autolink: true, HTMLAttributes: { target: null, rel: null } } }),
          Image,
          Placeholder.configure({ placeholder: () => this.placeholder() || this.translate.instant('editor.placeholder') }),
        ],
        content: this.value(),
        onUpdate: ({ editor }) => this.value.set(editor.isEmpty ? '' : editor.getHTML()),
        onTransaction: () => this.tick.update(n => n + 1),
      });
      this.tick.update(n => n + 1); // toolbar state now comes from the editor
    });
    // external value changes (loaded entity, restored draft) replace the content; our own updates are skipped
    effect(() => {
      const v = this.value();
      if (this.editor && v !== (this.editor.isEmpty ? '' : this.editor.getHTML())) this.editor.commands.setContent(v || '', { emitUpdate: false });
    });
  }

  ngOnDestroy(): void { this.editor?.destroy(); }

  private chain() { return this.editor!.chain().focus(); }
  active(name?: string, attrs?: Record<string, unknown>): boolean { this.tick(); return !!name && !!this.editor?.isActive(name, attrs); }
  can(what: 'undo' | 'redo'): boolean { this.tick(); return !!this.editor && (what === 'undo' ? this.editor.can().undo() : this.editor.can().redo()); }

  openLink(): void {
    const e = this.editor!;
    const href = (e.getAttributes('link')['href'] as string | undefined) ?? '';
    this.dialog.open(RichLinkDialog, { data: { href }, width: '640px' }).afterClosed().subscribe((r?: { href: string; title?: string } | 'remove') => {
      if (!r) return;
      if (r === 'remove') { this.chain().extendMarkRange('link').unsetLink().run(); return; }
      const internal = r.href.startsWith('/');
      const attrs = { href: r.href, target: internal ? null : '_blank', rel: internal ? null : 'noopener noreferrer nofollow' };
      if (e.state.selection.empty && !e.isActive('link')) this.chain().insertContent({ type: 'text', text: r.title || r.href, marks: [{ type: 'link', attrs }] }).run();
      else this.chain().extendMarkRange('link').setLink(attrs).run();
    });
  }

  openImage(): void {
    this.dialog.open(RichImageDialog, { data: this.context(), width: '760px' }).afterClosed().subscribe((r?: { src: string; alt: string }) => {
      if (r) this.chain().setImage({ src: r.src, alt: r.alt }).run();
    });
  }
}

const LINK_TYPES = ['page', 'harbour', 'ship', 'trip', 'logbook', 'post'];

/** Link dialog: external URL, or an internal target found by type + search. */
@Component({
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatInputModule, MatListModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'editor.link' | translate }}</h2>
    <mat-dialog-content>
      <mat-form-field appearance="outline" class="w"><mat-label>{{ 'editor.url' | translate }}</mat-label><input matInput [(ngModel)]="href" placeholder="https://… / /harbours/…" (keydown.enter)="ok()" /></mat-form-field>
      <h4>{{ 'editor.internal' | translate }}</h4>
      <mat-button-toggle-group [(ngModel)]="type" (change)="search()" hideSingleSelectionIndicator>
        @for (t of types; track t) { <mat-button-toggle [value]="t">{{ 'editor.types.' + t | translate }}</mat-button-toggle> }
      </mat-button-toggle-group>
      <mat-form-field appearance="outline" class="w search" subscriptSizing="dynamic"><mat-label>{{ 'common.search' | translate }}</mat-label><mat-icon matPrefix>search</mat-icon>
        <input matInput [(ngModel)]="q" (ngModelChange)="search()" /></mat-form-field>
      <mat-nav-list dense>
        @for (r of results(); track r.id) {
          <a mat-list-item (click)="pick(r)" [activated]="href === r.url"><span matListItemTitle>{{ r.title }}</span><span matListItemLine>{{ r.subtitle }} · {{ r.url }}</span></a>
        } @empty { <p class="muted">{{ 'editor.noResults' | translate }}</p> }
      </mat-nav-list>
      @if (error()) { <div class="error">{{ error() }}</div> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (data.href) { <button mat-button (click)="ref.close('remove')">{{ 'editor.unlink' | translate }}</button> }
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button (click)="ok()" [disabled]="!href.trim()">{{ 'editor.insert' | translate }}</button>
    </mat-dialog-actions>`,
  styles: `.w { width: 100%; } .search { margin-top: 8px; } mat-button-toggle-group { flex-wrap: wrap; } mat-nav-list { max-height: 260px; overflow: auto; }`,
})
export class RichLinkDialog {
  data = inject<{ href: string }>(MAT_DIALOG_DATA); ref = inject(MatDialogRef); private api = inject(Api);
  types = LINK_TYPES; type = 'page'; q = ''; href = this.data.href; title = '';
  results = signal<LinkTarget[]>([]); error = signal('');
  private timer?: ReturnType<typeof setTimeout>;
  constructor() { this.search(); }
  search(): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.api.get<LinkTarget[]>('/api/link-targets', { type: this.type, q: this.q }).subscribe({ next: r => { this.results.set(r); this.error.set(''); }, error: e => this.error.set(errorMessage(e)) }), 250);
  }
  pick(r: LinkTarget): void { this.href = r.url; this.title = r.title; }
  ok(): void { if (this.href.trim()) this.ref.close({ href: this.href.trim(), title: this.title }); }
}

/** Image dialog: uploads accessible at the content's level, plus upload into that context. */
@Component({
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule, MatProgressBarModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'editor.image' | translate }}</h2>
    <mat-dialog-content>
      <div class="row"><button mat-stroked-button type="button" (click)="file.click()"><mat-icon>upload</mat-icon> {{ 'editor.upload' | translate }}</button>
        <input #file type="file" accept="image/*" multiple hidden (change)="upload($event)" />
        <mat-form-field appearance="outline" class="grow" subscriptSizing="dynamic"><mat-label>{{ 'editor.alt' | translate }}</mat-label><input matInput [(ngModel)]="alt" /></mat-form-field></div>
      @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
      @if (error()) { <div class="error">{{ error() }}</div> }
      <div class="grid">
        @for (m of media(); track m.id) { <img [src]="m.thumbUrl" [alt]="m.caption ?? m.fileName" [title]="m.fileName" [class.sel]="selected()?.id === m.id" (click)="select(m)" (dblclick)="select(m); ok()" /> }
        @empty { @if (!busy()) { <p class="muted">{{ 'editor.noImages' | translate }}</p> } }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button (click)="ok()" [disabled]="!selected()">{{ 'editor.insert' | translate }}</button></mat-dialog-actions>`,
  styles: `.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); gap: 6px; margin-top: 12px; }
    .grid img { width: 100%; aspect-ratio: 1; object-fit: cover; border-radius: 6px; cursor: pointer; border: 3px solid transparent; box-sizing: border-box; }
    .grid img.sel { border-color: #1565c0; }`,
})
export class RichImageDialog {
  ctx = inject<RichContext>(MAT_DIALOG_DATA); private ref = inject(MatDialogRef); private api = inject(Api);
  media = signal<MediaPick[]>([]); selected = signal<MediaPick | null>(null); busy = signal(true); error = signal(''); alt = '';
  constructor() {
    const c = this.ctx;
    this.api.get<MediaPick[]>('/api/media', { shipId: c.shipId, tripId: c.tripId, logbookEntryId: c.logbookEntryId, postId: c.postId, mine: true })
      .subscribe({ next: m => { this.media.set(m); this.busy.set(false); }, error: e => { this.error.set(errorMessage(e)); this.busy.set(false); } });
  }
  select(m: MediaPick): void { this.selected.set(m); if (!this.alt) this.alt = m.caption ?? ''; }
  upload(e: Event): void {
    const input = e.target as HTMLInputElement; const files = Array.from(input.files ?? []); input.value = '';
    if (!files.length) return;
    const c = this.ctx; const fields: Record<string, string> = {};
    if (c.visibility) fields['visibility'] = c.visibility;
    if (!c.tripId && c.shipId) fields['shipId'] = c.shipId;
    this.busy.set(true); this.error.set('');
    this.api.upload<{ id: string; thumbUrl: string; previewUrl: string; caption: string | null }[]>(c.tripId ? `/api/trips/${c.tripId}/media` : '/api/media', files, fields).subscribe({
      next: r => {
        const added = r.map((m, i) => ({ id: m.id, thumbUrl: m.thumbUrl, url: m.previewUrl, fileName: files[i]?.name ?? '', caption: m.caption }));
        this.media.update(list => [...added, ...list]); this.selected.set(added[0] ?? null); this.busy.set(false);
      },
      error: err => { this.error.set(errorMessage(err)); this.busy.set(false); },
    });
  }
  ok(): void { const m = this.selected(); if (m) this.ref.close({ src: m.url, alt: this.alt }); }
}
