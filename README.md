# AdRackHub

Internal web app for managing customers, contacts, routes, stops, and recurring route billing.

## Stack

- **.NET 8** / ASP.NET Core MVC
- **Entity Framework Core 8** with **Microsoft SQL Server**
- Bootstrap 5 UI

## Quick Start

```bash
cd src/AdRackHub
dotnet run
```

Open [http://localhost:5281](http://localhost:5281).

## Core Model

| Entity | Purpose |
|--------|---------|
| **Customer** | Business client with optional Wave customer ID |
| **Contact** | Multiple contacts per customer (name, email, phone, address, role) |
| **Route** | Named route with price and recurring billing frequency |
| **Stop** | Physical location on a route |
| **CustomerRoute** | Links a customer to a route — all stops or custom stop selection |

## Contact Roles

Primary, Billing, Operations, Marketing, Other

## Billing

Each route has a **price** and **billing frequency** (Monthly, Quarterly, Annual). Customers are billed per route assignment.

## License

Internal use — AdRackHub
