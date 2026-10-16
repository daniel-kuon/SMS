import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import { Api } from '../../core/api';
import { TaskItem } from '../../core/models';
import { fmtDate } from '../../shared/shared';

/** Hierarchical to-dos per trip or ship (legacy jobs, spec 07 C6). */
@Component({
  selector: 'app-task-list',
  imports: [FormsModule, MatButtonModule, MatIconModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <form class="row" (ngSubmit)="add(null)">
      <mat-form-field appearance="outline" class="grow" subscriptSizing="dynamic"><mat-label>{{ 'task.new' | translate }}</mat-label><input matInput name="title" [(ngModel)]="title" /></mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'task.due' | translate }}</mat-label><input matInput type="date" name="due" [(ngModel)]="due" /></mat-form-field>
      <button mat-flat-button type="submit" [disabled]="!title">{{ 'common.new' | translate }}</button>
    </form>
    <ul class="tasks">
      @for (t of roots(); track t.id) {
        <li><mat-checkbox [checked]="t.done" (change)="toggle(t, $event.checked)"><span [class.done]="t.done">{{ t.title }}</span></mat-checkbox>
          @if (t.dueDate) { <span class="chip">{{ fmt(t.dueDate) }}</span> } @if (t.assignee) { <span class="chip">{{ t.assignee }}</span> }
          <button mat-icon-button (click)="sub(t)" title="{{ 'task.sub' | translate }}"><mat-icon>subdirectory_arrow_right</mat-icon></button>
          <button mat-icon-button (click)="remove(t)"><mat-icon>delete</mat-icon></button>
          <ul>@for (c of children(t.id); track c.id) {
            <li><mat-checkbox [checked]="c.done" (change)="toggle(c, $event.checked)"><span [class.done]="c.done">{{ c.title }}</span></mat-checkbox> @if (c.dueDate) { <span class="chip">{{ fmt(c.dueDate) }}</span> }
              <button mat-icon-button (click)="remove(c)"><mat-icon>delete</mat-icon></button></li> }</ul>
        </li>
      }
    </ul>`,
  styles: `.tasks, .tasks ul { list-style: none; padding-left: 0; } .tasks ul { padding-left: 32px; } .done { text-decoration: line-through; color: #888; } li { display: block; }`,
})
export class TaskList {
  private api = inject(Api);
  url = input.required<string>();
  tasks = signal<TaskItem[]>([]);
  title = ''; due = ''; fmt = fmtDate;
  ngOnInit(): void { this.load(); }
  load(): void { this.api.get<TaskItem[]>(this.url()).subscribe(t => this.tasks.set(t)); }
  roots(): TaskItem[] { return this.tasks().filter(t => !t.parentId); }
  children(id: string): TaskItem[] { return this.tasks().filter(t => t.parentId === id); }
  add(parentId: string | null, title = this.title): void {
    if (!title) return;
    this.api.post(this.url(), { title, dueDate: this.due || null, parentId, done: false }).subscribe(() => { this.title = ''; this.due = ''; this.load(); });
  }
  sub(t: TaskItem): void { const title = prompt('Unteraufgabe:'); if (title) this.add(t.id, title); }
  toggle(t: TaskItem, done: boolean): void { this.api.put(`/api/tasks/${t.id}`, { ...t, done }).subscribe(() => this.load()); }
  remove(t: TaskItem): void { this.api.delete(`/api/tasks/${t.id}`).subscribe(() => this.load()); }
}
