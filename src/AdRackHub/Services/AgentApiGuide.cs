using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AdRackHub.Services;

public static class AgentApiGuide
{
    public static string BaseUrl(HttpRequest request) => $"{request.Scheme}://{request.Host}";

    public static string Markdown(string baseUrl)
    {
        baseUrl = baseUrl.TrimEnd('/');
        return """
            # AdRackHub agent API

            Use this API to list, add, and update customers and prospects, including their contacts. It can also read open activities and log a stop visit with the driver and before and after photos.

            Base URL: `BASE_URL`

            ## How to connect

            ChatGPT Work plugins do not accept this API key as a custom header. Install the Ad-Rack plugin instead. It signs in with an AdRackHub admin account.

            1. Read this page: `BASE_URL/docs/agent.html`
            2. For a Custom GPT action, import `BASE_URL/api/agent/openapi.json`
            3. Ask the user for the API key. It is not written in this document.
            4. Send that key on every REST data request.

            ## ChatGPT Work plugin

            1. Open ChatGPT and go to Plugins.
            2. Select +.
            3. Select Add custom MCP server.
            4. Name the plugin Ad-Rack.
            5. Server URL: `BASE_URL/mcp`
            6. Authentication: OAuth.
            7. Select Create as a plugin, then install it.
            8. When ChatGPT asks to connect, sign in to AdRackHub as an admin and choose Allow.

            The plugin tools are `search_customers`, `get_customer`, `create_customer`, `update_customer`, `add_contact`, `update_contact`, `list_activities`, `billing_due`, `billing_outstanding`, `billing_forecast`, `invoice_reminder_template`, `search_stops`, `list_visits`, and `log_stop_visit`. New records default to Prospect. Creating a Customer, or changing a Prospect to Customer, assigns a brochure id and sends the customer to Wave. There is no delete tool.

            `list_activities` reads open Hub tasks and email follow-ups. `billing_due`, `billing_outstanding`, and `billing_forecast` read bills due, unpaid invoices, and the billing forecast. They do not send invoices or mark them received. `invoice_reminder_template` reads and renders the Admin invoice reminder. It does not save or send it. `log_stop_visit` records the driver and the before and after photos. Pass each email image attachment as base64.

            Search before creating. If create returns possible duplicates, do not create another record until the user picks one, or call create again with `acknowledgeDuplicateIds` set to every returned id.

            On updates, omit a field to leave it unchanged. Send null or an empty string to clear an optional field.

            ## REST API key

            The REST data API is separate from the plugin. If a data call returns HTTP 503 and says AgentApi:ApiKey is not configured, the server has no key. The page at `/docs/agent.html` and `/api/agent/openapi.json` stay available.

            Set one of these, then recycle the site app pool:

            - On the server, add `AgentApi:ApiKey` inside `appsettings.Production.json`. A deploy does not upload that file and does not replace it. Do not commit the key.
            - Or set the site environment variable `AgentApi__ApiKey` or `ADRACKHUB_AGENT_API_KEY`.

            On a development machine, put the key in `src/AdRackHub/appsettings.Development.local.json`. That file stays off git.

            Preferred header:

            ```
            Authorization: Bearer YOUR_KEY
            ```

            Alternate header:

            ```
            X-AdRackHub-Api-Key: YOUR_KEY
            ```

            Do not put the key in the URL. A missing or wrong key returns HTTP 401. If an administrator has not configured a key, the data endpoints return HTTP 503.

            ## What you can do

            - List customers and prospects. Every record includes its `contacts` array.
            - Get one record by id.
            - Create a customer or prospect, including contacts.
            - Update a customer or prospect.
            - Add a contact.
            - Update a contact.
            - List activities. Open tasks and email follow-ups are included by default.
            - Search stops and list visits.
            - Log a stop visit with the driver and before and after photos.
            - Read bills due, outstanding invoices, the billing forecast, and the invoice reminder template.

            Do not send DELETE. Delete requests return HTTP 405. This API cannot delete records.

            Billing can be read. This API cannot send invoices, mark them received, or change contracts. The invoice reminder template can be read and rendered. Saving it returns HTTP 403 unless `AgentApi:CanManageInvoiceReminderTemplate` is true. Warehouse bins are outside this API. Activities are read-only. A stop visit can be logged with photos.

            ## Records

            Customers, prospects, and expanded prospects are the same kind of record. `type` is `Customer`, `Prospect`, or `ExpandedProspect`.

            A contact's `name` is the business name. `firstName` and `lastName` are the person. `displayName` joins them for reading and cannot be written.

            `needsMoreInfo` is true when a street address or map coordinates are missing. It is calculated. Do not try to set it.

            `hubUrl` opens the record in AdRackHub. The viewer still has to be logged in.

            ## List

            `GET /api/agent/customers`

            Query parameters:

            - `type` — `Customer`, `Prospect`, `ExpandedProspect`, or `all`. `Prospect` includes expanded prospects. Omit it to return every type.
            - `status` — `Active`, `Inactive`, or `all`. Omit it to return every status.
            - `search` — matches the record id, business name, address, city, state, zip, phone, email, web URL, and contact name, title, email, and phone.
            - `page` — starts at 1. Default 1.
            - `pageSize` — default 50. Maximum 100.

            Response:

            ```json
            {
              "page": 1,
              "pageSize": 50,
              "total": 120,
              "totalPages": 3,
              "hasMore": true,
              "items": []
            }
            ```

            Each item includes `contacts`. Keep calling the next `page` while `hasMore` is true.

            ## Get one

            `GET /api/agent/customers/{id}`

            HTTP 404 when that id does not exist.

            ## Create

            `POST /api/agent/customers`

            Required fields: `customerName`, `type`. `type` must be `Customer` or `Prospect`. This API cannot assign `ExpandedProspect`.

            Defaults when omitted: `status` is `Active`, `invoiceReceiptMethod` is `Mail`.

            Optional fields: `address`, `city`, `state`, `zip`, `phone`, `email`, `webUrl`, `isHighValueProspect`, `isAtRisk`, `contacts`.

            Creating a record with `type` `Customer` assigns a brochure id and sends the customer to Wave, the same as adding a customer in AdRackHub. Use `Prospect` unless the business is a paying customer.

            `isHighValueProspect` applies to prospects only. `isAtRisk` applies to customers only.

            Unknown fields are ignored. These fields are read-only and will be rejected if you send them: `id`, `needsMoreInfo`, `latitude`, `longitude`, `accountManager`, `hubUrl`.

            Example:

            ```json
            {
              "customerName": "Riverside Museum",
              "type": "Prospect",
              "status": "Active",
              "address": "100 Main St",
              "city": "Paducah",
              "state": "KY",
              "zip": "42001",
              "phone": "270-555-0100",
              "email": "info@example.com",
              "webUrl": "https://example.com",
              "contacts": [
                {
                  "firstName": "Ada",
                  "lastName": "Lovelace",
                  "title": "Director",
                  "email": "ada@example.com",
                  "phone": "270-555-0101",
                  "cellPhone": "270-555-0199",
                  "role": "Primary",
                  "sendInvoice": false,
                  "linkedInUrl": "https://www.linkedin.com/in/ada-lovelace"
                }
              ]
            }
            ```

            On a contact, `name` defaults to the business name when omitted. `role` is `Primary`, `Billing`, `Operations`, `Marketing`, or `Other`. The default is `Primary`.

            `linkedInUrl` must be a profile URL such as `https://www.linkedin.com/in/name`. Search URLs are rejected. Omit the field when you do not have a profile.

            A title written in the last name as `Lovelace (Director)` is split into `lastName` and `title` when title is empty.

            The response is HTTP 201 and the saved record, including contact ids.

            ## Update a customer or prospect

            `PATCH /api/agent/customers/{id}`

            Send only the fields that should change. Omitted fields stay as they are. Send `null` or `""` to clear an optional text field.

            Do not send `contacts` on this request. Use the contact URLs below.

            Writable fields: `customerName`, `type`, `status`, `address`, `city`, `state`, `zip`, `phone`, `email`, `webUrl`, `invoiceReceiptMethod`, `isHighValueProspect`, `isAtRisk`.

            `type` can change between `Customer` and `Prospect`. Changing a prospect into a customer assigns a brochure id and sends it to Wave.

            `invoiceReceiptMethod` is `Mail`, `Email`, or `Both`.

            `PUT` is rejected. Use `PATCH`.

            Example:

            ```json
            {
              "phone": "270-555-0142",
              "city": "Louisville"
            }
            ```

            The response is HTTP 200 and the full record, including every contact.

            ## Add a contact

            `POST /api/agent/customers/{id}/contacts`

            The body is one contact object. `name` is optional and defaults to the business name.

            A contact with `sendInvoice` true and an email sets the parent `invoiceReceiptMethod` to `Both`.

            The response is HTTP 201 and the full customer or prospect, including every contact.

            ## Update a contact

            `PATCH /api/agent/customers/{id}/contacts/{contactId}`

            Use the same partial-update rules. The contact must belong to that customer id.

            Writable fields: `name`, `firstName`, `lastName`, `title`, `email`, `phone`, `cellPhone`, `address`, `city`, `state`, `zip`, `webUrl`, `linkedInUrl`, `role`, `sendInvoice`.

            The response is HTTP 200 and the full customer or prospect, including every contact.

            ## Activities

            `GET /api/agent/activities`

            Use this for a twice-daily review. The default `status` is `open`, which means `New` and `Working`. That includes tasks and email follow-ups along with other open notes.

            Query parameters:

            - `status` — `open` (default), `all`, `New`, `Working`, or `Done`.
            - `kind` — `Note`, `Call`, `Email`, `Meeting`, `Action`, `Task`, `BrochuresNeeded`, or `all`.
            - `customerId` — limit to one customer or prospect.
            - `search` — matches the note, the business name, or who logged it.
            - `page` and `pageSize` — same limits as the customer list.

            Each item includes `customerId`, `customerName`, `kind`, `status`, `dueDate`, `body`, `createdAt`, `createdBy`, `isOverdue`, and `hubUrl`.

            ## Stops

            `GET /api/agent/stops`

            `search` matches the stop name, route name, city, or highway exit. `routeId` limits the list to one route. Use the returned `id` as `stopId` when logging a visit.

            ## Visits

            `GET /api/agent/visits` lists recent visits. Optional filters are `stopId` and `routeId`.

            `POST /api/agent/visits` logs one visit. The plugin tool with the same job is `log_stop_visit`.

            ```json
            {
              "stopId": 12,
              "driverName": "Sam Rivera",
              "visitedAt": "2026-10-22T18:30:00Z",
              "notes": "Restocked the rack",
              "beforeFileName": "before.jpg",
              "beforeImageBase64": "<base64 from the email attachment>",
              "afterFileName": "after.jpg",
              "afterImageBase64": "<base64 from the email attachment>"
            }
            ```

            Send `stopId`, or `stopName` plus an optional `routeName` when that pair matches one stop. `driverName` is required. `visitedAt` is optional and defaults to now. Both photos are required. Each one is a JPG, PNG, GIF, or WEBP image, 8 MB or smaller. A data URL (`data:image/jpeg;base64,...`) is accepted. The bytes are checked, so a renamed file that is not an image is rejected.

            If several stops match, the response is HTTP 409 and nothing is saved. Call `GET /api/agent/stops` and send `stopId`.

            The saved visit includes the driver, who logged it, and links to the before and after photos. Those photo links require an AdRackHub login.

            ## Bills due

            `GET /api/agent/billing/due`

            Omit `year` and `month` for the current month. Send both together to choose another month. `search` matches the customer, contract, or route.

            `dueTotal` is the amount still to bill. `items` are those contracts. `submittedInvoices` are the invoices already created for that month, including received and canceled ones. This call does not send anything to Wave.

            ## Outstanding

            `GET /api/agent/billing/outstanding`

            Submitted invoices that have not been marked received. The aging buckets are `under30`, `from30To60`, and `over60`. `age` filters the list to `under30`, `30-60`, or `over60`. `weeks` is the minimum weeks late. `totalAmount` is the filtered list. The aging buckets stay based on the search, before the age and weeks filters.

            ## Forecast

            `GET /api/agent/billing/forecast`

            `months` defaults to 12 and cannot exceed 24. The forecast starts at the current month. `year` and `month` choose which month's contract lines are returned. `months` in the response is every month total. `lines` are the selected month. `sent` is true when that invoice was already submitted. `ignoreEndDate=true` keeps contracts after their end date.

            ## Invoice reminder template

            `GET /api/agent/communications/invoice-reminder`

            Returns the subject and body saved in Hub Admin, the merge fields, and a sample rendering. `canUpdate` is false unless the server has explicitly set `AgentApi:CanManageInvoiceReminderTemplate` to true.

            `POST /api/agent/communications/invoice-reminder/render`

            Fills the saved template, or the subject and body in the request, with sample data or one invoice. Send `invoiceId` to use that invoice. Omit `subject` and `body` to use the saved template. This call does not save anything and does not send email.

            `PUT /api/agent/communications/invoice-reminder`

            Replaces the saved Admin template. This returns HTTP 403 unless `AgentApi:CanManageInvoiceReminderTemplate` is true in `appsettings.Production.json` on the server. The plugin tool cannot save the template. Change the default in Hub under Admin, Communications, Invoice reminder template.

            ## Fields

            Customer or prospect:

            - `id` number, read-only
            - `customerName` string, required on create, max 200
            - `type` `Customer`, `Prospect`, or `ExpandedProspect`
            - `status` `Active` or `Inactive`
            - `isHighValueProspect` boolean
            - `isAtRisk` boolean
            - `needsMoreInfo` boolean, read-only
            - `address` max 300, `city` max 100, `state` max 50, `zip` max 20
            - `latitude` and `longitude` numbers, read-only
            - `phone` max 50, `email` max 200, `webUrl` max 500
            - `invoiceReceiptMethod` `Mail`, `Email`, or `Both`
            - `accountManager` string, read-only
            - `hubUrl` string, read-only
            - `contacts` array of contact objects

            Contact:

            - `id` and `customerId` numbers, read-only
            - `name` business name, max 200
            - `firstName` max 100, `lastName` max 100, `title` max 200
            - `displayName` read-only
            - `email` max 200, `phone` max 50, `cellPhone` max 50
            - `address` max 300, `city` max 100, `state` max 50, `zip` max 20
            - `webUrl` max 500, `linkedInUrl` max 500
            - `role` `Primary`, `Billing`, `Operations`, `Marketing`, or `Other`
            - `sendInvoice` boolean

            JSON names are camelCase. `CustomerName` is also accepted.

            ## Errors

            - `400` — the body lists `error` and `errors`
            - `401` — the API key is missing or wrong
            - `404` — no record with that id
            - `403` — the API key is valid, but changing the saved invoice reminder template is not allowed
            - `405` — delete, or PUT on a customer, is not available
            - `503` — the server has no API key configured

            Example:

            ```json
            {
              "error": "The request is invalid.",
              "errors": ["customerName is required.", "type is required and must be Customer or Prospect."]
            }
            ```

            ## Suggested flow

            1. `GET /api/agent/customers?search=riverside&type=Prospect`
            2. Read `items[].contacts` for the person, email, phone, and title.
            3. `PATCH /api/agent/customers/{id}` when the business details changed.
            4. `POST /api/agent/customers/{id}/contacts` when the person is missing.
            5. `PATCH /api/agent/customers/{id}/contacts/{contactId}` when the person is already there.
            """.Replace("BASE_URL", baseUrl);
    }

    public static string ToHtml(string markdown)
    {
        var sb = new StringBuilder();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inCode = false;
        var listKind = '\0';
        var paragraph = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraph.Length == 0)
                return;
            sb.Append("<p>").Append(Inline(paragraph.ToString().Trim())).Append("</p>");
            paragraph.Clear();
        }

        void CloseList()
        {
            if (listKind == '\0')
                return;
            sb.Append(listKind == 'o' ? "</ol>" : "</ul>");
            listKind = '\0';
        }

        foreach (var raw in lines)
        {
            if (raw.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                if (!inCode)
                {
                    inCode = true;
                    sb.Append("<pre><code>");
                }
                else
                {
                    inCode = false;
                    sb.Append("</code></pre>");
                }
                continue;
            }

            if (inCode)
            {
                sb.Append(WebUtility.HtmlEncode(raw)).Append('\n');
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                FlushParagraph();
                CloseList();
                continue;
            }

            if (raw.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                sb.Append("<h3>").Append(Inline(raw[4..])).Append("</h3>");
                continue;
            }

            if (raw.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                sb.Append("<h2>").Append(Inline(raw[3..])).Append("</h2>");
                continue;
            }

            if (raw.StartsWith("# ", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                sb.Append("<h2>").Append(Inline(raw[2..])).Append("</h2>");
                continue;
            }

            if (raw.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushParagraph();
                if (listKind != 'u')
                {
                    CloseList();
                    sb.Append("<ul>");
                    listKind = 'u';
                }
                sb.Append("<li>").Append(Inline(raw[2..])).Append("</li>");
                continue;
            }

            var ordered = OrderedItem.Match(raw);
            if (ordered.Success)
            {
                FlushParagraph();
                if (listKind != 'o')
                {
                    CloseList();
                    sb.Append("<ol>");
                    listKind = 'o';
                }
                sb.Append("<li>").Append(Inline(ordered.Groups[1].Value)).Append("</li>");
                continue;
            }

            if (paragraph.Length > 0)
                paragraph.Append(' ');
            paragraph.Append(raw.Trim());
        }

        FlushParagraph();
        CloseList();
        if (inCode)
            sb.Append("</code></pre>");
        return sb.ToString();
    }

    public static string OpenApiJson(string baseUrl)
    {
        var errorRef = "#/components/schemas/Error";
        var customerRef = "#/components/schemas/Customer";
        var writeRef = "#/components/schemas/CustomerWrite";
        var contactWriteRef = "#/components/schemas/ContactWrite";
        var activityListRef = "#/components/schemas/ActivityList";
        var stopListRef = "#/components/schemas/StopList";
        var visitListRef = "#/components/schemas/VisitList";
        var visitRef = "#/components/schemas/Visit";
        var visitWriteRef = "#/components/schemas/VisitWrite";
        var billingDueRef = "#/components/schemas/BillingDue";
        var billingOutstandingRef = "#/components/schemas/BillingOutstanding";
        var billingForecastRef = "#/components/schemas/BillingForecast";
        var reminderRef = "#/components/schemas/InvoiceReminderTemplate";
        var reminderRenderRef = "#/components/schemas/InvoiceReminderRender";
        var reminderWriteRef = "#/components/schemas/InvoiceReminderWrite";
        JsonObject ErrorResponse() => Response("Invalid request.", errorRef);
        JsonObject Unauthorized() => Response("Missing or wrong API key.", errorRef);
        JsonObject NotFound() => Response("No record with that id.", errorRef);

        var root = new JsonObject
        {
            ["openapi"] = "3.0.3",
            ["info"] = new JsonObject
            {
                ["title"] = "AdRackHub Agent API",
                ["version"] = "1.0.0",
                ["description"] = "List, create, and update customers and prospects, including contacts. Read activities, bills due, outstanding invoices, the billing forecast, and the invoice reminder template. Rendering a reminder does not save it. Updating the saved reminder requires AgentApi:CanManageInvoiceReminderTemplate. Log a stop visit with before and after photos. Delete is not available. Read /docs/agent.html for the full guide."
            },
            ["servers"] = new JsonArray { new JsonObject { ["url"] = baseUrl.TrimEnd('/') } },
            ["security"] = new JsonArray
            {
                new JsonObject { ["bearerAuth"] = new JsonArray() },
                new JsonObject { ["apiKeyAuth"] = new JsonArray() }
            },
            ["paths"] = new JsonObject
            {
                ["/api/agent/customers"] = new JsonObject
                {
                    ["get"] = Operation("listCustomers", "List customers and prospects, including contacts.", new JsonArray
                    {
                        QueryParam("type", "Customer, Prospect, ExpandedProspect, or all. Prospect includes expanded prospects."),
                        QueryParam("status", "Active, Inactive, or all."),
                        QueryParam("search", "Name, address, phone, email, id, or contact details."),
                        QueryParam("page", "Page number, starting at 1.", "integer"),
                        QueryParam("pageSize", "Default 50. Maximum 100.", "integer")
                    }, new JsonObject
                    {
                        ["200"] = Response("A page of records. Each item includes contacts.", "#/components/schemas/CustomerList"),
                        ["400"] = ErrorResponse(),
                        ["401"] = Unauthorized()
                    }),
                    ["post"] = Operation("createCustomer", "Create a customer or prospect. type Customer also sends the record to Wave.", null, writeRef, new JsonObject
                    {
                        ["201"] = Response("The saved record, including contacts.", customerRef),
                        ["400"] = ErrorResponse(),
                        ["401"] = Unauthorized()
                    })
                },
                ["/api/agent/customers/{id}"] = new JsonObject
                {
                    ["get"] = Operation("getCustomer", "Get one customer or prospect, including contacts.", new JsonArray { IdParam() }, null, new JsonObject
                    {
                        ["200"] = Response("The record, including contacts.", customerRef),
                        ["401"] = Unauthorized(),
                        ["404"] = NotFound()
                    }),
                    ["patch"] = Operation("updateCustomer", "Update fields on a customer or prospect. Send only fields that should change. Do not send contacts here.", new JsonArray { IdParam() }, writeRef, new JsonObject
                    {
                        ["200"] = Response("The updated record, including contacts.", customerRef),
                        ["400"] = ErrorResponse(),
                        ["401"] = Unauthorized(),
                        ["404"] = NotFound()
                    })
                },
                ["/api/agent/customers/{id}/contacts"] = new JsonObject
                {
                    ["post"] = Operation("createContact", "Add a contact. Returns the parent record with every contact.", new JsonArray { IdParam() }, contactWriteRef, new JsonObject
                    {
                        ["201"] = Response("The parent record, including every contact.", customerRef),
                        ["400"] = ErrorResponse(),
                        ["401"] = Unauthorized(),
                        ["404"] = NotFound()
                    })
                },
                ["/api/agent/customers/{id}/contacts/{contactId}"] = new JsonObject
                {
                    ["patch"] = Operation("updateContact", "Update one contact. Returns the parent record with every contact.", new JsonArray
                    {
                        IdParam(),
                        new JsonObject
                        {
                            ["name"] = "contactId",
                            ["in"] = "path",
                            ["required"] = true,
                            ["schema"] = new JsonObject { ["type"] = "integer" }
                        }
                    }, contactWriteRef, new JsonObject
                    {
                        ["200"] = Response("The parent record, including every contact.", customerRef),
                        ["400"] = ErrorResponse(),
                        ["401"] = Unauthorized(),
                        ["404"] = NotFound()
                    })
                },
                ["/api/agent/activities"] = new JsonObject
                {
                    ["get"] = Operation("listActivities", "List activities. Default status is open, which includes tasks and email follow-ups.", new JsonArray
                    {
                        QueryParam("status", "open, all, New, Working, or Done."),
                        QueryParam("kind", "Note, Call, Email, Meeting, Action, Task, BrochuresNeeded, or all."),
                        QueryParam("customerId", "Limit to one customer or prospect.", "integer"),
                        QueryParam("search", "Note text, business name, or who logged it."),
                        QueryParam("page", "Page number, starting at 1.", "integer"),
                        QueryParam("pageSize", "Default 50. Maximum 100.", "integer")
                    }, new JsonObject
                    {
                        ["200"] = Response("A page of activities.", activityListRef),
                        ["400"] = ErrorResponse(),
                        ["401"] = Unauthorized()
                    })
                },
                ["/api/agent/stops"] = new JsonObject
                {
                    ["get"] = Operation("searchStops", "Find a stop before logging a visit.", new JsonArray
                    {
                        QueryParam("search", "Stop name, route name, city, or highway exit."),
                        QueryParam("routeId", "Limit to one route.", "integer"),
                        QueryParam("page", "Page number, starting at 1.", "integer"),
                        QueryParam("pageSize", "Default 50. Maximum 100.", "integer")
                    }, new JsonObject
                    {
                        ["200"] = Response("A page of stops.", stopListRef),
                        ["401"] = Unauthorized()
                    })
                },
                ["/api/agent/visits"] = new JsonObject
                {
                    ["get"] = Operation("listVisits", "List stop visits, including the driver and photo links.", new JsonArray
                    {
                        QueryParam("stopId", "Limit to one stop.", "integer"),
                        QueryParam("routeId", "Limit to one route.", "integer"),
                        QueryParam("page", "Page number, starting at 1.", "integer"),
                        QueryParam("pageSize", "Default 50. Maximum 100.", "integer")
                    }, new JsonObject
                    {
                        ["200"] = Response("A page of visits.", visitListRef),
                        ["401"] = Unauthorized()
                    }),
                    ["post"] = Operation("logVisit", "Log a stop visit with the driver and before and after photos. Pass email attachments as base64.", null, visitWriteRef, new JsonObject
                    {
                        ["201"] = Response("The saved visit.", visitRef),
                        ["400"] = ErrorResponse(),
                        ["401"] = Unauthorized(),
                        ["404"] = NotFound(),
                        ["409"] = Response("More than one stop matches. Nothing was saved.", errorRef)
                    })
                },
                ["/api/agent/billing/due"] = new JsonObject
                {
                    ["get"] = Operation("billingDue", "Contracts due to be billed for a month, plus invoices already submitted. Does not send invoices.", new JsonArray
                    {
                        QueryParam("year", "Billing year. Send with month, or omit both for the current month.", "integer"),
                        QueryParam("month", "Billing month, 1 through 12.", "integer"),
                        QueryParam("search", "Customer, contract, or route name."),
                        QueryParam("page", "Page number, starting at 1.", "integer"),
                        QueryParam("pageSize", "Default 50. Maximum 100.", "integer")
                    }, new JsonObject
                    {
                        ["200"] = Response("Bills due for the month.", billingDueRef),
                        ["400"] = ErrorResponse(),
                        ["401"] = Unauthorized()
                    })
                },
                ["/api/agent/billing/outstanding"] = new JsonObject
                {
                    ["get"] = Operation("billingOutstanding", "Submitted invoices that have not been received, with aging.", new JsonArray
                    {
                        QueryParam("search", "Customer, contract, period, invoice id, or Wave invoice number."),
                        QueryParam("age", "under30, 30-60, or over60."),
                        QueryParam("weeks", "Minimum weeks late.", "integer"),
                        QueryParam("page", "Page number, starting at 1.", "integer"),
                        QueryParam("pageSize", "Default 50. Maximum 100.", "integer")
                    }, new JsonObject
                    {
                        ["200"] = Response("Outstanding invoices.", billingOutstandingRef),
                        ["400"] = ErrorResponse(),
                        ["401"] = Unauthorized()
                    })
                },
                ["/api/agent/billing/forecast"] = new JsonObject
                {
                    ["get"] = Operation("billingForecast", "Billing forecast by month, with contract lines for the selected month.", new JsonArray
                    {
                        QueryParam("year", "Selected year. Send with month, or omit both for the current month.", "integer"),
                        QueryParam("month", "Selected month, 1 through 12.", "integer"),
                        QueryParam("months", "How many months to forecast, from 1 to 24. Default 12.", "integer"),
                        QueryParam("ignoreEndDate", "When true, contracts past their end date stay in the forecast.", "boolean"),
                        QueryParam("page", "Page number for the selected month's lines.", "integer"),
                        QueryParam("pageSize", "Default 50. Maximum 100.", "integer")
                    }, new JsonObject
                    {
                        ["200"] = Response("The billing forecast.", billingForecastRef),
                        ["400"] = ErrorResponse(),
                        ["401"] = Unauthorized()
                    })
                },
                ["/api/agent/communications/invoice-reminder"] = new JsonObject
                {
                    ["get"] = Operation("invoiceReminder", "Read the Admin invoice reminder template, its merge fields, and a sample rendering. Does not send email.", new JsonArray(), new JsonObject
                    {
                        ["200"] = Response("The saved template and a sample rendering.", reminderRef),
                        ["401"] = Unauthorized()
                    }),
                    ["put"] = Operation("updateInvoiceReminder", "Replace the saved Admin template. Returns 403 unless AgentApi:CanManageInvoiceReminderTemplate is true.", null, reminderWriteRef, new JsonObject
                    {
                        ["200"] = Response("The saved template after the change.", reminderRef),
                        ["400"] = ErrorResponse(),
                        ["401"] = Unauthorized(),
                        ["403"] = Response("The API key cannot change the saved template.", errorRef)
                    })
                },
                ["/api/agent/communications/invoice-reminder/render"] = new JsonObject
                {
                    ["post"] = Operation("renderInvoiceReminder", "Fill the saved template, or the subject and body in the request, using sample data or one invoice. Does not save or send.", null, reminderRenderRef, new JsonObject
                    {
                        ["200"] = Response("The rendered subject and body. saved is false.", "#/components/schemas/InvoiceReminderRendered"),
                        ["400"] = ErrorResponse(),
                        ["401"] = Unauthorized(),
                        ["404"] = NotFound()
                    })
                }
            },
            ["components"] = new JsonObject
            {
                ["securitySchemes"] = new JsonObject
                {
                    ["bearerAuth"] = new JsonObject
                    {
                        ["type"] = "http",
                        ["scheme"] = "bearer"
                    },
                    ["apiKeyAuth"] = new JsonObject
                    {
                        ["type"] = "apiKey",
                        ["in"] = "header",
                        ["name"] = "X-AdRackHub-Api-Key"
                    }
                },
                ["schemas"] = new JsonObject
                {
                    ["CustomerList"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["page"] = IntProp(),
                            ["pageSize"] = IntProp(),
                            ["total"] = IntProp(),
                            ["totalPages"] = IntProp(),
                            ["hasMore"] = new JsonObject { ["type"] = "boolean" },
                            ["items"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["$ref"] = customerRef }
                            }
                        }
                    },
                    ["Customer"] = CustomerSchema(includeId: true),
                    ["CustomerWrite"] = CustomerSchema(includeId: false),
                    ["Contact"] = ContactSchema(includeId: true),
                    ["ContactWrite"] = ContactSchema(includeId: false),
                    ["ActivityList"] = PageSchema("#/components/schemas/Activity"),
                    ["Activity"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["id"] = IntProp(),
                            ["customerId"] = IntProp(),
                            ["customerName"] = StringProp("Business name."),
                            ["customerType"] = StringProp("Customer, Prospect, or ExpandedProspect."),
                            ["kind"] = StringProp("Note, Call, Email, Meeting, Action, Task, or BrochuresNeeded."),
                            ["status"] = StringProp("New, Working, or Done."),
                            ["dueDate"] = StringProp("yyyy-MM-dd, or null."),
                            ["body"] = StringProp("Activity text."),
                            ["createdAt"] = StringProp("When it was logged."),
                            ["createdBy"] = StringProp("Who logged it."),
                            ["isOverdue"] = new JsonObject { ["type"] = "boolean" },
                            ["hubUrl"] = StringProp("Link to the customer in AdRackHub.")
                        }
                    },
                    ["StopList"] = PageSchema("#/components/schemas/Stop"),
                    ["Stop"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["id"] = IntProp(),
                            ["stopName"] = StringProp("Stop name."),
                            ["routeId"] = IntProp(),
                            ["routeName"] = StringProp("Route name."),
                            ["city"] = StringProp("City."),
                            ["state"] = StringProp("State."),
                            ["highwayExit"] = StringProp("Highway or exit."),
                            ["lastVisitedAt"] = StringProp("Last visit time."),
                            ["hubUrl"] = StringProp("Link to the stop in AdRackHub.")
                        }
                    },
                    ["VisitList"] = PageSchema("#/components/schemas/Visit"),
                    ["Visit"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["id"] = IntProp(),
                            ["stopId"] = IntProp(),
                            ["stopName"] = StringProp("Stop name."),
                            ["routeId"] = IntProp(),
                            ["routeName"] = StringProp("Route name."),
                            ["visitedAt"] = StringProp("When the visit happened."),
                            ["driverName"] = StringProp("Driver who made the visit."),
                            ["visitedByName"] = StringProp("Who logged the visit."),
                            ["notes"] = StringProp("Optional note."),
                            ["hubUrl"] = StringProp("Link to the stop in AdRackHub."),
                            ["photos"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["$ref"] = "#/components/schemas/VisitPhoto" }
                            }
                        }
                    },
                    ["VisitPhoto"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["id"] = IntProp(),
                            ["kind"] = StringProp("Before or After."),
                            ["fileName"] = StringProp("Original file name."),
                            ["contentType"] = StringProp("Image type."),
                            ["hubUrl"] = StringProp("Photo link. Requires an AdRackHub login.")
                        }
                    },
                    ["VisitWrite"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["required"] = new JsonArray { "driverName", "beforeImageBase64", "afterImageBase64" },
                        ["properties"] = new JsonObject
                        {
                            ["stopId"] = IntProp(),
                            ["routeName"] = StringProp("Used with stopName when stopId is omitted."),
                            ["stopName"] = StringProp("Used when stopId is omitted."),
                            ["driverName"] = StringProp("Driver who made the visit."),
                            ["visitedAt"] = StringProp("ISO-8601 date and time. Omit for now."),
                            ["notes"] = StringProp("Optional note, up to 1000 characters."),
                            ["beforeFileName"] = StringProp("Original before file name."),
                            ["beforeContentType"] = StringProp("Optional. The image type is detected from the bytes."),
                            ["beforeImageBase64"] = StringProp("Before photo from the email, as base64 or a data URL."),
                            ["afterFileName"] = StringProp("Original after file name."),
                            ["afterContentType"] = StringProp("Optional. The image type is detected from the bytes."),
                            ["afterImageBase64"] = StringProp("After photo from the email, as base64 or a data URL.")
                        }
                    },
                    ["BillingDue"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["year"] = IntProp(),
                            ["month"] = IntProp(),
                            ["periodLabel"] = StringProp("Month label."),
                            ["dueTotal"] = NumberProp("Amount still to bill."),
                            ["dueCustomerCount"] = IntProp(),
                            ["dueContractCount"] = IntProp(),
                            ["page"] = IntProp(),
                            ["pageSize"] = IntProp(),
                            ["total"] = IntProp(),
                            ["totalPages"] = IntProp(),
                            ["hasMore"] = new JsonObject { ["type"] = "boolean" },
                            ["hubUrl"] = StringProp("Billing page for this month."),
                            ["items"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["$ref"] = "#/components/schemas/BillingDueItem" } },
                            ["submittedInvoices"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["$ref"] = "#/components/schemas/BillingInvoice" } }
                        }
                    },
                    ["BillingDueItem"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["customerId"] = IntProp(),
                            ["customerName"] = StringProp("Customer name."),
                            ["contractId"] = IntProp(),
                            ["contractName"] = StringProp("Contract name."),
                            ["term"] = StringProp("Billing term."),
                            ["nextBillDate"] = StringProp("yyyy-MM-dd."),
                            ["routes"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                            ["amount"] = NumberProp("Amount due for this contract."),
                            ["hubUrl"] = StringProp("Customer page.")
                        }
                    },
                    ["BillingInvoice"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["invoiceId"] = IntProp(),
                            ["customerId"] = IntProp(),
                            ["customerName"] = StringProp("Customer name."),
                            ["status"] = StringProp("Submitted, Received, or Canceled."),
                            ["periodLabel"] = StringProp("Billing month."),
                            ["waveInvoiceNumber"] = StringProp("Wave invoice number."),
                            ["amount"] = NumberProp("Invoice total."),
                            ["contractNames"] = StringProp("Contracts on the invoice."),
                            ["hubUrl"] = StringProp("Customer page.")
                        }
                    },
                    ["BillingOutstanding"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["asOf"] = StringProp("Aging date, yyyy-MM-dd."),
                            ["totalAmount"] = NumberProp("Filtered outstanding total."),
                            ["invoiceCount"] = IntProp(),
                            ["clientCount"] = IntProp(),
                            ["under30"] = new JsonObject { ["$ref"] = "#/components/schemas/BillingAging" },
                            ["from30To60"] = new JsonObject { ["$ref"] = "#/components/schemas/BillingAging" },
                            ["over60"] = new JsonObject { ["$ref"] = "#/components/schemas/BillingAging" },
                            ["page"] = IntProp(),
                            ["pageSize"] = IntProp(),
                            ["total"] = IntProp(),
                            ["totalPages"] = IntProp(),
                            ["hasMore"] = new JsonObject { ["type"] = "boolean" },
                            ["hubUrl"] = StringProp("Outstanding invoices page."),
                            ["items"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["$ref"] = "#/components/schemas/BillingOutstandingItem" } }
                        }
                    },
                    ["BillingAging"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["amount"] = NumberProp("Amount in this age bucket."),
                            ["invoiceCount"] = IntProp()
                        }
                    },
                    ["BillingOutstandingItem"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["invoiceId"] = IntProp(),
                            ["customerId"] = IntProp(),
                            ["customerName"] = StringProp("Customer name."),
                            ["periodLabel"] = StringProp("Billing month."),
                            ["invoiceDate"] = StringProp("yyyy-MM-dd."),
                            ["daysLate"] = IntProp(),
                            ["weeksLate"] = IntProp(),
                            ["waveInvoiceNumber"] = StringProp("Wave invoice number."),
                            ["contractNames"] = StringProp("Contracts on the invoice."),
                            ["amount"] = NumberProp("Invoice total."),
                            ["hubUrl"] = StringProp("Customer page.")
                        }
                    },
                    ["BillingForecast"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["forecastTotal"] = NumberProp("Total across the forecast months."),
                            ["forecastInvoiceCount"] = IntProp(),
                            ["selectedYear"] = IntProp(),
                            ["selectedMonth"] = IntProp(),
                            ["selectedPeriodLabel"] = StringProp("Selected month."),
                            ["ignoreEndDate"] = new JsonObject { ["type"] = "boolean" },
                            ["hubUrl"] = StringProp("Forecast page."),
                            ["months"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["$ref"] = "#/components/schemas/BillingForecastMonth" } },
                            ["page"] = IntProp(),
                            ["pageSize"] = IntProp(),
                            ["total"] = IntProp(),
                            ["totalPages"] = IntProp(),
                            ["hasMore"] = new JsonObject { ["type"] = "boolean" },
                            ["lines"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["$ref"] = "#/components/schemas/BillingForecastLine" } }
                        }
                    },
                    ["BillingForecastMonth"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["year"] = IntProp(),
                            ["month"] = IntProp(),
                            ["label"] = StringProp("Month label."),
                            ["invoiceCount"] = IntProp(),
                            ["customerCount"] = IntProp(),
                            ["amount"] = NumberProp("Forecast amount for the month."),
                            ["isCurrentMonth"] = new JsonObject { ["type"] = "boolean" },
                            ["isSelected"] = new JsonObject { ["type"] = "boolean" }
                        }
                    },
                    ["BillingForecastLine"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["customerId"] = IntProp(),
                            ["customerName"] = StringProp("Customer name."),
                            ["contractId"] = IntProp(),
                            ["contractName"] = StringProp("Contract name."),
                            ["term"] = StringProp("Billing term."),
                            ["contractEndDate"] = StringProp("yyyy-MM-dd, or null."),
                            ["amount"] = NumberProp("Forecast amount."),
                            ["sent"] = new JsonObject { ["type"] = "boolean", ["description"] = "True when the invoice was already submitted." },
                            ["hubUrl"] = StringProp("Customer page.")
                        }
                    },
                    ["InvoiceReminderTemplate"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["subject"] = StringProp("Saved subject, including merge fields."),
                            ["body"] = StringProp("Saved body, including merge fields."),
                            ["updatedAt"] = StringProp("UTC timestamp of the last save, or null for the system default."),
                            ["updatedBy"] = StringProp("Who last saved it, or null for the system default."),
                            ["canUpdate"] = new JsonObject { ["type"] = "boolean", ["description"] = "True only when AgentApi:CanManageInvoiceReminderTemplate is set." },
                            ["renderedSubject"] = StringProp("Sample or invoice rendering of the subject."),
                            ["renderedBody"] = StringProp("Sample or invoice rendering of the body."),
                            ["renderSource"] = StringProp("sample or invoice."),
                            ["invoiceId"] = IntProp(),
                            ["customerName"] = StringProp("Customer used for the rendering."),
                            ["mergeFields"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["$ref"] = "#/components/schemas/InvoiceReminderMergeField" } },
                            ["hubUrl"] = StringProp("Admin page for the template.")
                        }
                    },
                    ["InvoiceReminderMergeField"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["token"] = StringProp("Merge field, such as {{Invoice number}}."),
                            ["description"] = StringProp("What the field becomes.")
                        }
                    },
                    ["InvoiceReminderRender"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["invoiceId"] = IntProp(),
                            ["subject"] = StringProp("Optional subject. Omit to use the saved template."),
                            ["body"] = StringProp("Optional body. Omit to use the saved template.")
                        }
                    },
                    ["InvoiceReminderRendered"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["subject"] = StringProp("Rendered subject."),
                            ["body"] = StringProp("Rendered body."),
                            ["source"] = StringProp("sample or invoice."),
                            ["invoiceId"] = IntProp(),
                            ["customerName"] = StringProp("Customer used for the rendering."),
                            ["saved"] = new JsonObject { ["type"] = "boolean", ["description"] = "Always false. Rendering does not save." },
                            ["hubUrl"] = StringProp("Admin page for the template.")
                        }
                    },
                    ["InvoiceReminderWrite"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["required"] = new JsonArray { "subject", "body" },
                        ["properties"] = new JsonObject
                        {
                            ["subject"] = StringProp("New saved subject."),
                            ["body"] = StringProp("New saved body.")
                        }
                    },
                    ["Error"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["error"] = new JsonObject { ["type"] = "string" },
                            ["errors"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["type"] = "string" }
                            }
                        }
                    }
                }
            }
        };

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            root.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static JsonObject Operation(string operationId, string summary, JsonArray? parameters, string? bodyRef, JsonObject responses)
    {
        var operation = new JsonObject
        {
            ["operationId"] = operationId,
            ["summary"] = summary,
            ["responses"] = responses
        };
        if (parameters != null)
            operation["parameters"] = parameters;
        if (bodyRef != null)
        {
            operation["requestBody"] = new JsonObject
            {
                ["required"] = true,
                ["content"] = new JsonObject
                {
                    ["application/json"] = new JsonObject
                    {
                        ["schema"] = new JsonObject { ["$ref"] = bodyRef }
                    }
                }
            };
        }
        return operation;
    }

    private static JsonObject Operation(string operationId, string summary, JsonArray parameters, JsonObject responses) =>
        Operation(operationId, summary, parameters, null, responses);

    private static JsonObject Response(string description, string schemaRef) => new()
    {
        ["description"] = description,
        ["content"] = new JsonObject
        {
            ["application/json"] = new JsonObject
            {
                ["schema"] = new JsonObject { ["$ref"] = schemaRef }
            }
        }
    };

    private static JsonObject QueryParam(string name, string description, string type = "string") => new()
    {
        ["name"] = name,
        ["in"] = "query",
        ["required"] = false,
        ["description"] = description,
        ["schema"] = new JsonObject { ["type"] = type }
    };

    private static JsonObject IdParam() => new()
    {
        ["name"] = "id",
        ["in"] = "path",
        ["required"] = true,
        ["schema"] = new JsonObject { ["type"] = "integer" }
    };

    private static JsonObject PageSchema(string itemRef) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["page"] = IntProp(),
            ["pageSize"] = IntProp(),
            ["total"] = IntProp(),
            ["totalPages"] = IntProp(),
            ["hasMore"] = new JsonObject { ["type"] = "boolean" },
            ["items"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["$ref"] = itemRef }
            }
        }
    };

    private static JsonObject IntProp() => new() { ["type"] = "integer" };

    private static JsonObject NumberProp(string description) => new()
    {
        ["type"] = "number",
        ["description"] = description
    };

    private static JsonObject StringProp(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description
    };

    private static JsonObject CustomerSchema(bool includeId)
    {
        var properties = new JsonObject();
        if (includeId)
        {
            properties["id"] = IntProp();
            properties["needsMoreInfo"] = new JsonObject { ["type"] = "boolean" };
            properties["latitude"] = new JsonObject { ["type"] = "number", ["nullable"] = true };
            properties["longitude"] = new JsonObject { ["type"] = "number", ["nullable"] = true };
            properties["accountManager"] = StringProp("Read-only.");
            properties["hubUrl"] = StringProp("Read-only link to the record in AdRackHub.");
        }

        properties["customerName"] = StringProp("Business name.");
        properties["type"] = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray { "Customer", "Prospect", "ExpandedProspect" },
            ["description"] = "Create and update accept Customer or Prospect. ExpandedProspect is read-only."
        };
        properties["status"] = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray { "Active", "Inactive" }
        };
        properties["isHighValueProspect"] = new JsonObject { ["type"] = "boolean", ["description"] = "Prospects only." };
        properties["isAtRisk"] = new JsonObject { ["type"] = "boolean", ["description"] = "Customers only." };
        properties["address"] = StringProp("Street address.");
        properties["city"] = StringProp("City.");
        properties["state"] = StringProp("State.");
        properties["zip"] = StringProp("Postal code.");
        properties["phone"] = StringProp("Main phone.");
        properties["email"] = StringProp("Main email.");
        properties["webUrl"] = StringProp("Website.");
        properties["invoiceReceiptMethod"] = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray { "Mail", "Email", "Both" }
        };
        properties["contacts"] = new JsonObject
        {
            ["type"] = "array",
            ["description"] = includeId
                ? "Every contact on the record."
                : "Contacts to create with the record. Leave this out when updating.",
            ["items"] = new JsonObject { ["$ref"] = includeId ? "#/components/schemas/Contact" : "#/components/schemas/ContactWrite" }
        };

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        if (!includeId)
            schema["required"] = new JsonArray { "customerName", "type" };
        return schema;
    }

    private static JsonObject ContactSchema(bool includeId)
    {
        var properties = new JsonObject();
        if (includeId)
        {
            properties["id"] = IntProp();
            properties["customerId"] = IntProp();
            properties["displayName"] = StringProp("Read-only.");
        }

        properties["name"] = StringProp("Business name. Defaults to the customer or prospect name.");
        properties["firstName"] = StringProp("Person first name.");
        properties["lastName"] = StringProp("Person last name.");
        properties["title"] = StringProp("Job title.");
        properties["email"] = StringProp("Email.");
        properties["phone"] = StringProp("Telephone.");
        properties["cellPhone"] = StringProp("Cell phone.");
        properties["address"] = StringProp("Street address.");
        properties["city"] = StringProp("City.");
        properties["state"] = StringProp("State.");
        properties["zip"] = StringProp("Postal code.");
        properties["webUrl"] = StringProp("Website.");
        properties["linkedInUrl"] = StringProp("LinkedIn profile URL, https://www.linkedin.com/in/...");
        properties["role"] = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray { "Primary", "Billing", "Operations", "Marketing", "Other" }
        };
        properties["sendInvoice"] = new JsonObject { ["type"] = "boolean" };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
    }

    private static string Inline(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        encoded = CodeSpan.Replace(encoded, "<code>$1</code>");
        encoded = BoldSpan.Replace(encoded, "<strong>$1</strong>");
        return encoded;
    }

    private static readonly Regex OrderedItem = new(@"^\d+\.\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex CodeSpan = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex BoldSpan = new(@"\*\*([^*]+)\*\*", RegexOptions.Compiled);
}
