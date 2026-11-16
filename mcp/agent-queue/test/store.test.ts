import assert from "node:assert/strict";
import test from "node:test";
import { QueueStore } from "../src/store.js";
import { enqueueHubTask, enqueueProspect } from "../src/tools.js";

function memoryStore(): QueueStore {
  return QueueStore.open(":memory:");
}

test("prospect keyword payload is stored for the worker", () => {
  const store = memoryStore();
  const item = enqueueProspect(store, {
    name: "Ada Lovelace",
    company: "Analytical Engines",
    email: "ada@example.com",
    phone: "270-555-0100",
    source: "phone",
    notes: "Met at KTIA",
    created_by: "cursor",
  });
  assert.equal(item.task_type, "prospect_creation");
  assert.equal(item.status, "pending");
  assert.equal(item.assigned_agent, "chatgpt-worker");
  assert.equal(item.created_by, "cursor");
  assert.equal(item.payload.name, "Ada Lovelace");
  assert.equal(item.payload.company, "Analytical Engines");
  assert.match(item.instructions, /Create a new prospect/);
  assert.match(item.created_at, /^\d{4}-\d{2}-\d{2}T/);
  store.close();
});

test("hub task defaults the account to Ad-rack", () => {
  const store = memoryStore();
  const item = enqueueHubTask(store, { task_title: "Call about racks", client_or_prospect: "  " });
  assert.equal(item.task_type, "hub_task");
  assert.equal(item.payload.client_or_prospect, "Ad-rack");
  assert.match(item.instructions, /Ad-rack/);
  store.close();
});

test("claim takes the oldest high-priority item first", () => {
  const store = memoryStore();
  enqueueProspect(store, { name: "Low", priority: "low" });
  const highOld = enqueueProspect(store, { name: "High old", priority: "high" });
  enqueueProspect(store, { name: "Medium", priority: "medium" });
  enqueueProspect(store, { name: "High new", priority: "high" });

  const first = store.claimNext();
  const second = store.claimNext();
  assert.equal(first?.id, highOld.id);
  assert.equal(first?.status, "in_progress");
  assert.equal(first?.assigned_agent, "chatgpt-worker");
  assert.equal(second?.payload.name, "High new");
  store.close();
});

test("two claims do not return the same item", () => {
  const store = memoryStore();
  enqueueProspect(store, { name: "Only" });
  const claimed = store.claimNext("worker-a");
  const empty = store.claimNext("worker-b");
  assert.equal(claimed?.assigned_agent, "worker-a");
  assert.equal(empty, null);
  store.close();
});

test("complete, fail, and retry", () => {
  const store = memoryStore();
  const item = enqueueProspect(store, { name: "Retry me" });
  const claimed = store.claimNext();
  assert.ok(claimed);
  const failed = store.fail(item.id, "Hub timed out", true);
  assert.equal(failed.status, "pending");
  assert.equal(failed.error_log, "Hub timed out");
  const again = store.claimNext();
  assert.equal(again?.id, item.id);
  const done = store.complete(item.id, "Created prospect 42");
  assert.equal(done.status, "completed");
  assert.equal(done.result_output, "Created prospect 42");
  const dead = store.fail(enqueueProspect(store, { name: "Nope" }).id, "bad data", false);
  assert.equal(dead.status, "failed");
  store.close();
});

test("list filters by status and type", () => {
  const store = memoryStore();
  enqueueProspect(store, { name: "Lead" });
  enqueueHubTask(store, { task_title: "Follow up", client_or_prospect: "Paducah CVB" });
  const pendingProspects = store.list({ status: "pending", task_type: "prospect_creation" });
  assert.equal(pendingProspects.length, 1);
  assert.equal(pendingProspects[0]?.payload.name, "Lead");
  store.close();
});
