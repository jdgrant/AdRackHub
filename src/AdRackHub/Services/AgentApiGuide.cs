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

            Use this API to list, add, and update customers and prospects, including their contacts.

            Base URL: `BASE_URL`

            ## How to connect

            1. Read this page: `BASE_URL/docs/agent.md`
            2. For a Custom GPT action, import `BASE_URL/api/agent/openapi.json`
            3. Ask the user for the API key. It is not written in this document.
            4. Send that key on every data request.

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

            Do not send DELETE. Delete requests return HTTP 405. This API cannot delete customers, prospects, or contacts.

            Billing, contracts, routes, stops, warehouse bins, notes, and tasks are outside this API.

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
            - `405` — delete, or PUT, is not available
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
                ["description"] = "List, create, and update customers and prospects, including contacts. Delete is not available. Read /docs/agent.md for the full guide."
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

    private static JsonObject IntProp() => new() { ["type"] = "integer" };

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
