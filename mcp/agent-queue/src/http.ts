import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import { createMcpExpressApp } from "@modelcontextprotocol/sdk/server/express.js";
import type { Request, Response, NextFunction } from "express";
import { timingSafeEqual } from "node:crypto";
import { createQueueServer } from "./server.js";
import { QueueStore } from "./store.js";
import { enqueueGeneral, enqueueHubTask, enqueueProspect } from "./tools.js";
import { isStatus, isTaskType, type Priority, type Status, type TaskType } from "./types.js";

const token = process.env.MCP_AUTH_TOKEN?.trim();
if (!token) {
  console.error("MCP_AUTH_TOKEN is required for the HTTP transport.");
  process.exit(1);
}
const authToken = token;

const host = process.env.MCP_HOST?.trim() || "127.0.0.1";
const port = Number(process.env.MCP_PORT || "8765");
const store = QueueStore.open();
const app = createMcpExpressApp({ host });

app.use((req: Request, res: Response, next: NextFunction) => {
  if (req.path === "/health") {
    next();
    return;
  }
  const header = req.header("authorization") ?? "";
  if (!bearerMatches(header, authToken)) {
    res.status(401).json({ error: "Unauthorized" });
    return;
  }
  next();
});

app.get("/health", (_req: Request, res: Response) => {
  res.json({ ok: true });
});

app.post("/queue/prospects", (req: Request, res: Response) => {
  respond(res, () =>
    enqueueProspect(store, {
      name: String(req.body?.name ?? ""),
      company: req.body?.company,
      email: req.body?.email,
      phone: req.body?.phone,
      source: req.body?.source,
      notes: req.body?.notes,
      priority: req.body?.priority as Priority | undefined,
      created_by: req.body?.created_by,
    }),
  );
});

app.post("/queue/hub-tasks", (req: Request, res: Response) => {
  respond(res, () =>
    enqueueHubTask(store, {
      task_title: String(req.body?.task_title ?? ""),
      client_or_prospect: req.body?.client_or_prospect,
      description: req.body?.description,
      due_date: req.body?.due_date,
      priority: req.body?.priority as Priority | undefined,
      created_by: req.body?.created_by,
    }),
  );
});

app.post("/queue/general", (req: Request, res: Response) => {
  respond(res, () =>
    enqueueGeneral(store, {
      title: String(req.body?.title ?? ""),
      instructions: String(req.body?.instructions ?? ""),
      priority: req.body?.priority as Priority | undefined,
      created_by: req.body?.created_by,
    }),
  );
});

app.post("/queue/claim", (req: Request, res: Response) => {
  respond(res, () => ({ item: store.claimNext(req.body?.agent_name) }));
});

app.post("/queue/items/:id/complete", (req: Request, res: Response) => {
  respond(res, () => store.complete(routeId(req), String(req.body?.result_output ?? "")));
});

app.post("/queue/items/:id/fail", (req: Request, res: Response) => {
  respond(res, () => store.fail(routeId(req), String(req.body?.error_log ?? ""), Boolean(req.body?.retry)));
});

app.get("/queue", (req: Request, res: Response) => {
  respond(res, () => {
    const status = queryString(req.query.status);
    const taskType = queryString(req.query.task_type);
    if (status && !isStatus(status)) throw new Error(`Unknown status '${status}'.`);
    if (taskType && !isTaskType(taskType)) throw new Error(`Unknown task_type '${taskType}'.`);
    const limit = req.query.limit == null ? undefined : Number(req.query.limit);
    return {
      items: store.list({
        status: status as Status | undefined,
        task_type: taskType as TaskType | undefined,
        limit,
      }),
    };
  });
});

app.post("/mcp", async (req: Request, res: Response) => {
  const server = createQueueServer(store);
  try {
    const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined });
    await server.connect(transport);
    await transport.handleRequest(req, res, req.body);
    res.on("close", () => {
      void transport.close();
      void server.close();
    });
  } catch (error) {
    console.error(error);
    if (!res.headersSent) {
      res.status(500).json({
        jsonrpc: "2.0",
        error: { code: -32603, message: "Internal server error" },
        id: null,
      });
    }
  }
});

app.get("/mcp", (_req: Request, res: Response) => {
  res.status(405).json({
    jsonrpc: "2.0",
    error: { code: -32000, message: "Method not allowed. Send POST requests to /mcp." },
    id: null,
  });
});

app.listen(port, host, () => {
  console.log(`Agent queue listening on http://${host}:${port}`);
  console.log(`MCP streamable HTTP: http://${host}:${port}/mcp`);
});

function respond(res: Response, action: () => unknown): void {
  try {
    res.status(200).json(action());
  } catch (error) {
    const message = error instanceof Error ? error.message : "Request failed.";
    const status = message.includes("was not found") ? 404 : 400;
    res.status(status).json({ error: message });
  }
}

function routeId(req: Request): string {
  const value = req.params.id;
  return Array.isArray(value) ? value[0] ?? "" : value;
}

function queryString(value: unknown): string | undefined {
  if (typeof value !== "string") return undefined;
  const text = value.trim();
  return text || undefined;
}

function bearerMatches(header: string, expectedToken: string): boolean {
  const expected = Buffer.from(`Bearer ${expectedToken}`);
  const actual = Buffer.from(header);
  if (expected.length !== actual.length) return false;
  return timingSafeEqual(expected, actual);
}
