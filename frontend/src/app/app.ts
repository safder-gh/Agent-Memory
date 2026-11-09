import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  ChatApiService,
  ChatMessage,
  ChatSession,
  ChatSessionSummary,
} from './chat-api.service';

@Component({
  imports: [DatePipe, FormsModule],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  private readonly chatApi = inject(ChatApiService);

  protected readonly sessions = signal<ChatSessionSummary[]>([]);
  protected readonly activeSession = signal<ChatSession | null>(null);
  protected readonly messages = signal<ChatMessage[]>([]);
  protected readonly draft = signal('');
  protected readonly loading = signal(false);
  protected readonly sending = signal(false);
  protected readonly error = signal('');

  constructor() {
    void this.loadSessions();
  }

  protected async loadSessions(): Promise<void> {
    this.loading.set(true);
    this.error.set('');

    try {
      this.sessions.set(await this.chatApi.listSessions());
    } catch (error) {
      this.error.set(this.errorMessage(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async createSession(): Promise<void> {
    if (this.sending()) {
      return;
    }

    this.error.set('');

    try {
      const session = await this.chatApi.createSession();
      this.activeSession.set(session);
      this.messages.set([]);
      this.sessions.update((sessions) => [session, ...sessions]);
    } catch (error) {
      this.error.set(this.errorMessage(error));
    }
  }

  protected async openSession(summary: ChatSessionSummary): Promise<void> {
    if (this.sending()) {
      return;
    }

    this.error.set('');
    this.loading.set(true);

    try {
      const session = await this.chatApi.getSession(summary.id);
      this.activeSession.set(session);
      this.messages.set(session.messages);
    } catch (error) {
      this.error.set(this.errorMessage(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async deleteSession(
    event: MouseEvent,
    session: ChatSessionSummary,
  ): Promise<void> {
    event.stopPropagation();

    if (this.sending()) {
      return;
    }

    if (!window.confirm(`Delete "${session.title}" and its messages?`)) {
      return;
    }

    this.error.set('');

    try {
      await this.chatApi.deleteSession(session.id);
      this.sessions.update((sessions) =>
        sessions.filter((item) => item.id !== session.id),
      );

      if (this.activeSession()?.id === session.id) {
        this.activeSession.set(null);
        this.messages.set([]);
      }
    } catch (error) {
      this.error.set(this.errorMessage(error));
    }
  }

  protected async sendMessage(event: SubmitEvent): Promise<void> {
    event.preventDefault();
    const session = this.activeSession();
    const content = this.draft().trim();

    if (!session || !content || this.sending()) {
      return;
    }

    const now = new Date().toISOString();
    this.error.set('');
    this.sending.set(true);
    this.draft.set('');
    this.messages.update((messages) => [
      ...messages,
      { id: crypto.randomUUID(), role: 'user', content, createdAtUtc: now },
      {
        id: crypto.randomUUID(),
        role: 'assistant',
        content: '',
        createdAtUtc: now,
      },
    ]);

    try {
      await this.chatApi.streamMessage(session.id, content, (text) => {
        this.messages.update((messages) => {
          const updated = [...messages];
          const last = updated.at(-1);
          if (last?.role === 'assistant') {
            updated[updated.length - 1] = {
              ...last,
              content: last.content + text,
            };
          }
          return updated;
        });
      });

      const detail = await this.chatApi.getSession(session.id);
      this.messages.set(detail.messages);
      this.activeSession.set(detail);
      this.sessions.update((sessions) =>
        sessions.map((item) =>
          item.id === detail.id
            ? {
                id: detail.id,
                title: detail.title,
                createdAtUtc: detail.createdAtUtc,
                updatedAtUtc: detail.updatedAtUtc,
              }
            : item,
        ),
      );
    } catch (error) {
      const message = this.errorMessage(error);
      this.messages.update((messages) =>
        messages.filter(
          (message, index) =>
            !(index === messages.length - 1 && message.role === 'assistant' && !message.content),
        ),
      );
      await this.loadSessions();
      this.error.set(message);
    } finally {
      this.sending.set(false);
    }
  }

  private errorMessage(error: unknown): string {
    return error instanceof Error ? error.message : 'Something went wrong.';
  }
}
