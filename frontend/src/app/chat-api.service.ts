import { Injectable } from '@angular/core';

export interface ChatSessionSummary {
  id: string;
  title: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  createdAtUtc: string;
}

export interface ChatSession extends ChatSessionSummary {
  messages: ChatMessage[];
}

@Injectable({ providedIn: 'root' })
export class ChatApiService {
  private readonly baseUrl = 'http://localhost:5156/api';

  async listSessions(): Promise<ChatSessionSummary[]> {
    return this.request<ChatSessionSummary[]>(`${this.baseUrl}/sessions`);
  }

  async createSession(): Promise<ChatSession> {
    return this.request<ChatSession>(`${this.baseUrl}/sessions`, {
      method: 'POST',
    });
  }

  async getSession(id: string): Promise<ChatSession> {
    return this.request<ChatSession>(
      `${this.baseUrl}/sessions/${encodeURIComponent(id)}`,
    );
  }

  async deleteSession(id: string): Promise<void> {
    const response = await fetch(
      `${this.baseUrl}/sessions/${encodeURIComponent(id)}`,
      { method: 'DELETE' },
    );

    if (!response.ok) {
      throw new Error(await this.responseError(response));
    }
  }

  async streamMessage(
    sessionId: string,
    content: string,
    onText: (text: string) => void,
  ): Promise<void> {
    const response = await fetch(
      `${this.baseUrl}/sessions/${encodeURIComponent(sessionId)}/messages`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content }),
      },
    );

    if (!response.ok) {
      throw new Error(await this.responseError(response));
    }

    if (!response.body || !response.headers.get('content-type')?.includes('text/event-stream')) {
      throw new Error('The API did not return a streaming response.');
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    const processFrame = (frame: string): void => {
      let eventName = 'message';
      const data: string[] = [];

      for (const line of frame.split('\n')) {
        if (line.startsWith('event:')) {
          eventName = line.slice(6).trim();
        } else if (line.startsWith('data:')) {
          data.push(line.slice(5).trimStart());
        }
      }

      if (data.length === 0) {
        return;
      }

      const payload = JSON.parse(data.join('\n')) as { text?: string; message?: string };
      if (eventName === 'delta' && payload.text) {
        onText(payload.text);
      } else if (eventName === 'error') {
        throw new Error(payload.message ?? 'The assistant could not complete this response.');
      }
    };

    try {
      while (true) {
        const { done, value } = await reader.read();
        buffer += decoder.decode(value, { stream: !done }).replace(/\r\n/g, '\n');

        let boundary = buffer.indexOf('\n\n');
        while (boundary !== -1) {
          processFrame(buffer.slice(0, boundary));
          buffer = buffer.slice(boundary + 2);
          boundary = buffer.indexOf('\n\n');
        }

        if (done) {
          if (buffer.trim()) {
            processFrame(buffer);
          }
          break;
        }
      }
    } finally {
      reader.releaseLock();
    }
  }

  private async request<T>(url: string, init?: RequestInit): Promise<T> {
    const response = await fetch(url, init);
    if (!response.ok) {
      throw new Error(await this.responseError(response));
    }
    return (await response.json()) as T;
  }

  private async responseError(response: Response): Promise<string> {
    const body = (await response.json().catch(() => null)) as
      | { detail?: string; message?: string }
      | null;
    return (
      body?.detail ??
      body?.message ??
      `The API request failed with status ${response.status}.`
    );
  }
}
