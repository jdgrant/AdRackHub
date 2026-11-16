export const TASK_TYPES = ["prospect_creation", "hub_task", "general"] as const;
export const PRIORITIES = ["low", "medium", "high"] as const;
export const STATUSES = ["pending", "in_progress", "completed", "failed", "needs_review"] as const;

export type TaskType = (typeof TASK_TYPES)[number];
export type Priority = (typeof PRIORITIES)[number];
export type Status = (typeof STATUSES)[number];

export interface QueueItem {
  id: string;
  task_type: TaskType;
  title: string;
  payload: Record<string, unknown>;
  instructions: string;
  priority: Priority;
  status: Status;
  assigned_agent: string;
  created_by: string;
  result_output: string | null;
  error_log: string | null;
  created_at: string;
  updated_at: string;
}

export interface NewItem {
  task_type: TaskType;
  title: string;
  payload: Record<string, unknown>;
  instructions: string;
  priority?: Priority;
  created_by?: string;
  assigned_agent?: string;
}

export interface ListFilter {
  status?: Status;
  task_type?: TaskType;
  limit?: number;
}

export function isTaskType(value: string): value is TaskType {
  return (TASK_TYPES as readonly string[]).includes(value);
}

export function isPriority(value: string): value is Priority {
  return (PRIORITIES as readonly string[]).includes(value);
}

export function isStatus(value: string): value is Status {
  return (STATUSES as readonly string[]).includes(value);
}
