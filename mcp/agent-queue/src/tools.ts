import type { QueueItem } from "./types.js";
import type { QueueStore } from "./store.js";

const DEFAULT_CLIENT = "Ad-rack";

export function enqueueProspect(
  store: QueueStore,
  input: {
    name: string;
    company?: string;
    email?: string;
    phone?: string;
    source?: string;
    notes?: string;
    priority?: "low" | "medium" | "high";
    created_by?: string;
  },
): QueueItem {
  const name = required(input.name, "name");
  const company = optional(input.company);
  const email = optional(input.email);
  const phone = optional(input.phone);
  const source = optional(input.source);
  const notes = optional(input.notes);
  const payload = { name, company, email, phone, source, notes };
  const title = company ? `Prospect: ${name} (${company})` : `Prospect: ${name}`;
  const instructions = [
    "Create a new prospect record in the AdRack Hub.",
    `Name: ${name}`,
    `Company: ${company ?? "(none)"}`,
    `Email: ${email ?? "(none)"}`,
    `Phone: ${phone ?? "(none)"}`,
    `Source: ${source ?? "(none)"}`,
    `Notes: ${notes ?? "(none)"}`,
    "Use the company as the Hub business name when it is present. Otherwise use the name. Search for an existing prospect before creating one.",
  ].join("\n");
  return store.enqueue({
    task_type: "prospect_creation",
    title,
    payload,
    instructions,
    priority: input.priority,
    created_by: input.created_by,
  });
}

export function enqueueHubTask(
  store: QueueStore,
  input: {
    task_title: string;
    client_or_prospect?: string;
    description?: string;
    due_date?: string;
    priority?: "low" | "medium" | "high";
    created_by?: string;
  },
): QueueItem {
  const taskTitle = required(input.task_title, "task_title");
  const client = optional(input.client_or_prospect) || DEFAULT_CLIENT;
  const description = optional(input.description);
  const dueDate = optional(input.due_date);
  const payload = {
    task_title: taskTitle,
    client_or_prospect: client,
    description,
    due_date: dueDate,
  };
  const instructions = [
    "Look up the Hub account and attach this task to it.",
    `Account: ${client}`,
    `Task: ${taskTitle}`,
    `Due: ${dueDate ?? "(none)"}`,
    `Details: ${description ?? "(none)"}`,
    client === DEFAULT_CLIENT
      ? `No other account was named, so the account is ${DEFAULT_CLIENT}.`
      : `If ${client} is not found, look up ${DEFAULT_CLIENT} only when the named account cannot be matched.`,
  ].join("\n");
  return store.enqueue({
    task_type: "hub_task",
    title: taskTitle,
    payload,
    instructions,
    priority: input.priority,
    created_by: input.created_by,
  });
}

export function enqueueGeneral(
  store: QueueStore,
  input: {
    title: string;
    instructions: string;
    priority?: "low" | "medium" | "high";
    created_by?: string;
  },
): QueueItem {
  return store.enqueue({
    task_type: "general",
    title: required(input.title, "title"),
    payload: {},
    instructions: required(input.instructions, "instructions"),
    priority: input.priority,
    created_by: input.created_by,
  });
}

function required(value: string, name: string): string {
  const text = (value ?? "").trim();
  if (!text) throw new Error(`${name} is required.`);
  return text;
}

function optional(value: string | undefined): string | null {
  const text = (value ?? "").trim();
  return text ? text : null;
}
