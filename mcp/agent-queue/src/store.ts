import { randomUUID } from "node:crypto";
import { mkdirSync } from "node:fs";
import path from "node:path";
import Database from "better-sqlite3";
import {
  isPriority,
  isStatus,
  isTaskType,
  type ListFilter,
  type NewItem,
  type Priority,
  type QueueItem,
  type Status,
  type TaskType,
} from "./types.js";

interface QueueRow {
  id: string;
  task_type: string;
  title: string;
  payload: string;
  instructions: string;
  priority: string;
  status: string;
  assigned_agent: string;
  created_by: string;
  result_output: string | null;
  error_log: string | null;
  created_at: string;
  updated_at: string;
}

const PRIORITY_ORDER = `CASE priority WHEN 'high' THEN 0 WHEN 'medium' THEN 1 WHEN 'low' THEN 2 ELSE 3 END`;

export class QueueStore {
  constructor(private readonly db: Database.Database) {
    db.pragma("journal_mode = WAL");
    db.pragma("busy_timeout = 5000");
    db.exec(`
      CREATE TABLE IF NOT EXISTS queue_items (
        id TEXT PRIMARY KEY,
        task_type TEXT NOT NULL CHECK (task_type IN ('prospect_creation', 'hub_task', 'general')),
        title TEXT NOT NULL,
        payload TEXT NOT NULL,
        instructions TEXT NOT NULL,
        priority TEXT NOT NULL CHECK (priority IN ('low', 'medium', 'high')),
        status TEXT NOT NULL CHECK (status IN ('pending', 'in_progress', 'completed', 'failed', 'needs_review')),
        assigned_agent TEXT NOT NULL DEFAULT 'chatgpt-worker',
        created_by TEXT NOT NULL,
        result_output TEXT,
        error_log TEXT,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL
      );
      CREATE INDEX IF NOT EXISTS queue_items_claim
        ON queue_items (status, priority, created_at);
    `);
  }

  static open(file = defaultDatabasePath()): QueueStore {
    if (file !== ":memory:") {
      mkdirSync(path.dirname(file), { recursive: true });
    }
    return new QueueStore(new Database(file));
  }

  close(): void {
    this.db.close();
  }

  enqueue(input: NewItem): QueueItem {
    const priority = input.priority ?? "medium";
    if (!isPriority(priority)) {
      throw new Error(`priority must be low, medium, or high.`);
    }
    const now = isoNow();
    const id = randomUUID();
    this.db
      .prepare(
        `INSERT INTO queue_items (
          id, task_type, title, payload, instructions, priority, status,
          assigned_agent, created_by, result_output, error_log, created_at, updated_at
        ) VALUES (
          @id, @task_type, @title, @payload, @instructions, @priority, 'pending',
          @assigned_agent, @created_by, NULL, NULL, @created_at, @updated_at
        )`,
      )
      .run({
        id,
        task_type: input.task_type,
        title: requiredText(input.title, "title"),
        payload: JSON.stringify(input.payload ?? {}),
        instructions: requiredText(input.instructions, "instructions"),
        priority,
        assigned_agent: clean(input.assigned_agent) || "chatgpt-worker",
        created_by: clean(input.created_by) || "unknown",
        created_at: now,
        updated_at: now,
      });
    return this.require(id);
  }

  claimNext(agentName = "chatgpt-worker"): QueueItem | null {
    const agent = clean(agentName) || "chatgpt-worker";
    const claim = this.db.transaction(() => {
      const row = this.db
        .prepare(
          `SELECT id FROM queue_items
           WHERE status = 'pending'
           ORDER BY ${PRIORITY_ORDER}, created_at ASC
           LIMIT 1`,
        )
        .get() as { id: string } | undefined;
      if (!row) return null;
      const now = isoNow();
      const updated = this.db
        .prepare(
          `UPDATE queue_items
           SET status = 'in_progress', assigned_agent = ?, updated_at = ?
           WHERE id = ? AND status = 'pending'`,
        )
        .run(agent, now, row.id);
      if (updated.changes !== 1) return null;
      return this.require(row.id);
    });
    return claim.immediate();
  }

  complete(taskId: string, resultOutput: string): QueueItem {
    const id = requiredText(taskId, "task_id");
    const now = isoNow();
    const updated = this.db
      .prepare(
        `UPDATE queue_items
         SET status = 'completed', result_output = ?, updated_at = ?
         WHERE id = ?`,
      )
      .run(resultOutput ?? "", now, id);
    if (updated.changes !== 1) throw new Error(`Queue item ${id} was not found.`);
    return this.require(id);
  }

  fail(taskId: string, errorLog: string, retry = false): QueueItem {
    const id = requiredText(taskId, "task_id");
    const status: Status = retry ? "pending" : "failed";
    const now = isoNow();
    const updated = this.db
      .prepare(
        `UPDATE queue_items
         SET status = ?, error_log = ?, updated_at = ?
         WHERE id = ?`,
      )
      .run(status, errorLog ?? "", now, id);
    if (updated.changes !== 1) throw new Error(`Queue item ${id} was not found.`);
    return this.require(id);
  }

  list(filter: ListFilter = {}): QueueItem[] {
    const status = filter.status;
    const taskType = filter.task_type;
    if (status && !isStatus(status)) throw new Error(`Unknown status '${status}'.`);
    if (taskType && !isTaskType(taskType)) throw new Error(`Unknown task_type '${taskType}'.`);
    const limit = clampLimit(filter.limit);
    const rows = this.db
      .prepare(
        `SELECT * FROM queue_items
         WHERE (? IS NULL OR status = ?)
           AND (? IS NULL OR task_type = ?)
         ORDER BY updated_at DESC, created_at DESC
         LIMIT ?`,
      )
      .all(status ?? null, status ?? null, taskType ?? null, taskType ?? null, limit) as QueueRow[];
    return rows.map(toItem);
  }

  private require(id: string): QueueItem {
    const row = this.db.prepare(`SELECT * FROM queue_items WHERE id = ?`).get(id) as QueueRow | undefined;
    if (!row) throw new Error(`Queue item ${id} was not found.`);
    return toItem(row);
  }
}

export function defaultDatabasePath(): string {
  return process.env.AGENT_QUEUE_DB?.trim() || path.resolve(process.cwd(), "data", "queue.sqlite");
}

export function isoNow(): string {
  return new Date().toISOString();
}

function toItem(row: QueueRow): QueueItem {
  return {
    id: row.id,
    task_type: row.task_type as TaskType,
    title: row.title,
    payload: JSON.parse(row.payload) as Record<string, unknown>,
    instructions: row.instructions,
    priority: row.priority as Priority,
    status: row.status as Status,
    assigned_agent: row.assigned_agent,
    created_by: row.created_by,
    result_output: row.result_output,
    error_log: row.error_log,
    created_at: row.created_at,
    updated_at: row.updated_at,
  };
}

function requiredText(value: string, name: string): string {
  const text = clean(value);
  if (!text) throw new Error(`${name} is required.`);
  return text;
}

function clean(value: string | null | undefined): string {
  return (value ?? "").trim();
}

function clampLimit(limit: number | undefined): number {
  if (limit == null || Number.isNaN(limit)) return 25;
  return Math.min(100, Math.max(1, Math.trunc(limit)));
}
