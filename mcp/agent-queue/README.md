# AdRack agent queue

Shared queue any MCP client can write to. A ChatGPT worker claims the items and applies them to the Hub.

Say **Add prospect** to queue a lead. Say **Add task** to queue a Hub to-do. A task with no account name is stored on **Ad-rack**. Anything else can be queued with the general tool.

## Run

```bash
cd mcp/agent-queue
npm install
npm test
npm run build
npm start
```

`npm start` speaks MCP over stdio for Cursor. The SQLite file is `data/queue.sqlite` unless `AGENT_QUEUE_DB` is set.

Remote clients use Streamable HTTP. Set a bearer token first. Do not commit it.

```bash
MCP_AUTH_TOKEN=replace-me npm run start:http
```

The MCP endpoint is `http://127.0.0.1:8765/mcp`. The same actions are REST routes documented in `openapi.yaml` for a Custom GPT Action. `GET /health` does not require the token.

`WORKER_PROMPT.md` is the system prompt for the ChatGPT worker.

## Cursor

This repo ignores `.cursor/`. The local config is `.cursor/mcp.json`. The same snippet is tracked in `cursor.mcp.json`:

```json
{
  "mcpServers": {
    "agent-queue": {
      "command": "node",
      "args": ["mcp/agent-queue/dist/stdio.js"],
      "env": {
        "AGENT_QUEUE_DB": "mcp/agent-queue/data/queue.sqlite"
      }
    }
  }
}
```

A remote Cursor client can use the HTTP endpoint instead:

```json
{
  "mcpServers": {
    "agent-queue": {
      "url": "http://127.0.0.1:8765/mcp",
      "headers": {
        "Authorization": "Bearer ${env:MCP_AUTH_TOKEN}"
      }
    }
  }
}
```

## Tools

| Tool | When |
| --- | --- |
| `enqueue_prospect` | User says "Add prospect" or asks to log a lead |
| `enqueue_hub_task` | User says "Add task". Account defaults to Ad-rack |
| `enqueue_general_item` | Anything that is not a prospect or a Hub task |
| `claim_next_task` | Worker takes the oldest high, then medium, then low pending item |
| `complete_task` | Worker finished and records the Hub id |
| `fail_task` | Worker failed. `retry` true puts it back to pending |
| `list_queue` | Audit by status or task type |
