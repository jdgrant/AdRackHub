import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import * as z from "zod/v4";
import type { QueueStore } from "./store.js";
import { enqueueGeneral, enqueueHubTask, enqueueProspect } from "./tools.js";

const prioritySchema = z
  .enum(["low", "medium", "high"])
  .default("medium")
  .describe("low, medium, or high. Default medium.");

const createdBySchema = z
  .string()
  .default("unknown")
  .describe("Which AI or client added the item. Examples: cursor, gemini, claude, chatgpt.");

export function createQueueServer(store: QueueStore): McpServer {
  const server = new McpServer({
    name: "adrack-agent-queue",
    version: "1.0.0",
  });

  server.registerTool(
    "enqueue_prospect",
    {
      title: "Add prospect",
      description:
        'TRIGGER THIS TOOL whenever the user says "Add prospect" or asks to log a new lead or prospect into the Hub. Do not wait for the user to name this tool. Sets task_type to prospect_creation and stores name, company, email, phone, source, and notes in payload for the Hub worker.',
      inputSchema: {
        name: z.string().describe("Person or lead name."),
        company: z.string().optional().describe("Business or organization, when the user gives one."),
        email: z.string().optional().describe("Email address, when the user gives one."),
        phone: z.string().optional().describe("Phone number, when the user gives one."),
        source: z.string().optional().describe("Where the lead came from, when the user says so."),
        notes: z.string().optional().describe("Extra context the user included."),
        priority: prioritySchema,
        created_by: createdBySchema,
      },
    },
    async (args) => ok(enqueueProspect(store, args)),
  );

  server.registerTool(
    "enqueue_hub_task",
    {
      title: "Add task",
      description:
        'TRIGGER THIS TOOL whenever the user says "Add task" or asks to assign a to-do in the Hub. Do not wait for the user to name this tool. If the user names a client or prospect, pass that exact name as client_or_prospect. If the user does not name one, you must set client_or_prospect to "Ad-rack". Never leave it blank and never invent a different default. Sets task_type to hub_task.',
      inputSchema: {
        task_title: z.string().describe("Short title of the to-do."),
        client_or_prospect: z
          .string()
          .default("Ad-rack")
          .describe('Hub account to attach the task to. Required default is "Ad-rack" when the user names nobody.'),
        description: z.string().optional().describe("What the task should accomplish."),
        due_date: z.string().optional().describe("Due date as the user stated it, preferably YYYY-MM-DD."),
        priority: prioritySchema,
        created_by: createdBySchema,
      },
    },
    async (args) => ok(enqueueHubTask(store, args)),
  );

  server.registerTool(
    "enqueue_general_item",
    {
      title: "Queue general item",
      description:
        'Fallback queue item. Use this only when the request is not "Add prospect" and not "Add task", and it is not a request to log a lead or assign a Hub to-do. Sets task_type to general. Put the full request in instructions.',
      inputSchema: {
        title: z.string().describe("Short summary of the queue item."),
        instructions: z.string().describe("Full natural-language instructions for the worker."),
        priority: prioritySchema,
        created_by: createdBySchema,
      },
    },
    async (args) => ok(enqueueGeneral(store, args)),
  );

  server.registerTool(
    "claim_next_task",
    {
      title: "Claim next queue item",
      description:
        "Atomically claim the oldest highest-priority pending item. High is taken before medium, then low. Within one priority, the oldest created_at wins. Sets status to in_progress and assigned_agent to agent_name. Returns the full record, or item null when the queue is empty. The worker should call this in a loop until item is null.",
      inputSchema: {
        agent_name: z.string().default("chatgpt-worker").describe("Worker identity. Default chatgpt-worker."),
      },
    },
    async ({ agent_name }) => ok({ item: store.claimNext(agent_name) }),
  );

  server.registerTool(
    "complete_task",
    {
      title: "Complete queue item",
      description:
        "Mark a claimed queue item completed. Put the Hub record id or a short confirmation in result_output.",
      inputSchema: {
        task_id: z.string().describe("Queue item id returned by claim_next_task."),
        result_output: z.string().describe("Hub record id or confirmation summary, as Markdown or plain text."),
      },
    },
    async ({ task_id, result_output }) => ok(store.complete(task_id, result_output)),
  );

  server.registerTool(
    "fail_task",
    {
      title: "Fail queue item",
      description:
        "Record a failure. retry true puts the item back to pending so a later claim can try again. retry false marks it failed. error_log stores the reason.",
      inputSchema: {
        task_id: z.string().describe("Queue item id."),
        error_log: z.string().describe("Why the work failed."),
        retry: z.boolean().default(false).describe("True resets status to pending. False marks the item failed."),
      },
    },
    async ({ task_id, error_log, retry }) => ok(store.fail(task_id, error_log, retry)),
  );

  server.registerTool(
    "list_queue",
    {
      title: "List queue",
      description:
        "List queue items for an audit or status check. Filter by status and task_type when the user asks. Default limit is 25.",
      inputSchema: {
        status: z
          .enum(["pending", "in_progress", "completed", "failed", "needs_review"])
          .optional()
          .describe("Optional status filter."),
        task_type: z
          .enum(["prospect_creation", "hub_task", "general"])
          .optional()
          .describe("Optional task type filter."),
        limit: z.number().int().min(1).max(100).default(25).describe("Maximum rows. Default 25."),
      },
    },
    async ({ status, task_type, limit }) => ok({ items: store.list({ status, task_type, limit }) }),
  );

  return server;
}

function ok(value: unknown) {
  return {
    content: [{ type: "text" as const, text: JSON.stringify(value, null, 2) }],
  };
}
