import assert from "node:assert/strict";
import test from "node:test";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { InMemoryTransport } from "@modelcontextprotocol/sdk/inMemory.js";
import { createQueueServer } from "../src/server.js";
import { QueueStore } from "../src/store.js";

test("MCP tools advertise the keyword triggers and enqueue a prospect", async () => {
  const store = QueueStore.open(":memory:");
  const server = createQueueServer(store);
  const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair();
  await server.connect(serverTransport);
  const client = new Client({ name: "test-client", version: "1.0.0" });
  await client.connect(clientTransport);

  const listed = await client.listTools();
  const byName = new Map(listed.tools.map((tool) => [tool.name, tool.description ?? ""]));
  assert.match(byName.get("enqueue_prospect") ?? "", /Add prospect/);
  assert.match(byName.get("enqueue_hub_task") ?? "", /Add task/);
  assert.match(byName.get("enqueue_hub_task") ?? "", /Ad-rack/);
  assert.match(byName.get("enqueue_general_item") ?? "", /Fallback/);

  const result = await client.callTool({
    name: "enqueue_prospect",
    arguments: { name: "Port Clinton Lighthouse", created_by: "claude" },
  });
  const text = textContent(result);
  const item = JSON.parse(text) as { task_type: string; created_by: string; payload: { name: string } };
  assert.equal(item.task_type, "prospect_creation");
  assert.equal(item.created_by, "claude");
  assert.equal(item.payload.name, "Port Clinton Lighthouse");

  const claimed = await client.callTool({ name: "claim_next_task", arguments: {} });
  const claim = JSON.parse(textContent(claimed)) as { item: { payload: { name: string } } | null };
  assert.equal(claim.item?.payload.name, "Port Clinton Lighthouse");

  const empty = await client.callTool({ name: "claim_next_task", arguments: {} });
  const none = JSON.parse(textContent(empty)) as { item: unknown };
  assert.equal(none.item, null);

  await client.close();
  await server.close();
  store.close();
});

function textContent(result: { content?: Array<{ type: string; text?: string }> }): string {
  const block = result.content?.find((item) => item.type === "text");
  assert.ok(block?.text);
  return block.text;
}
