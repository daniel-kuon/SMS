import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { RichEditor } from './rich-editor';
import { richLinkTarget } from '../app';

describe('RichEditor', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [RichEditor], providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideTranslateService()] }));

  it('renders the bound HTML, emits edits as HTML and follows external value changes', async () => {
    const fixture = TestBed.createComponent(RichEditor);
    fixture.componentRef.setInput('value', '<p>Hallo</p>');
    fixture.detectChanges(); await fixture.whenStable();
    const c = fixture.componentInstance;
    expect(c.editor!.getHTML()).toBe('<p>Hallo</p>');

    c.editor!.chain().selectAll().toggleBold().run();
    expect(c.value()).toBe('<p><strong>Hallo</strong></p>');

    fixture.componentRef.setInput('value', '<h2>Neu</h2><p><a href="/harbours/1">Kiel</a></p>');
    fixture.detectChanges(); await fixture.whenStable();
    expect(c.editor!.getHTML()).toBe('<h2>Neu</h2><p><a href="/harbours/1">Kiel</a></p>');
  });
});

describe('richLinkTarget', () => {
  function click(html: string, init: MouseEventInit = {}): string | null {
    const div = document.createElement('div'); div.innerHTML = html; document.body.appendChild(div);
    const a = div.querySelector('a')!; let result: string | null = null;
    a.addEventListener('click', e => { result = richLinkTarget(e); e.preventDefault(); });
    a.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0, ...init }));
    div.remove(); return result;
  }
  it('routes internal links in rich text only', () => {
    expect(click('<div class="rich"><a href="/harbours/1">x</a></div>')).toBe('/harbours/1');
    expect(click('<div class="rich"><a href="https://x.org">x</a></div>')).toBeNull();
    expect(click('<div class="rich"><a href="/api/media/1/original">x</a></div>')).toBeNull();
    expect(click('<div class="rich"><a href="/p/x" target="_blank">x</a></div>')).toBeNull();
    expect(click('<div class="rich"><a href="/p/x">x</a></div>', { ctrlKey: true })).toBeNull();
    expect(click('<div><a href="/p/x">x</a></div>')).toBeNull();
    expect(click('<div class="rich"><div contenteditable="true"><a href="/p/x">x</a></div></div>')).toBeNull();
  });
});
