import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { createQueueServer } from "./server.js";
import { QueueStore } from "./store.js";

const store = QueueStore.open();
const server = createQueueServer(store);
await server.connect(new StdioServerTransport());
