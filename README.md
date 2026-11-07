# HelpDesk Ticket System

A help desk and ticketing web app built with **ASP.NET Core 10 Razor Pages**, EF Core (SQLite), ASP.NET Core Identity, SignalR and Bootstrap 5.

## Features

- **Three roles** — Admin, Agent and Client, with an admin area and a customer portal
- **Tickets** — create, track, filter, search, sort, bulk-update and export to CSV; unseen tickets are highlighted as NEW
- **Auto-assignment** — new tickets go to the least-busy agent, preferring the ticket's department
- **SLA tracking** — first-reply and resolution deadlines per priority, overdue flags and admin alerts
- **Conversation** — public replies, internal notes hidden from customers, canned replies, file attachments
- **Email** — outgoing SMTP notifications and email-to-ticket over IMAP, both configured in the app
- **Real-time notifications** — notification bell and pop-ups via SignalR
- **Knowledge base** — searchable articles, "was this helpful?" voting, article suggestions while a ticket is written
- **Customer satisfaction** — 1–5 star ratings on resolved tickets
- **Reports** — response and resolution times, SLA compliance, agent performance, breakdowns by department, category, priority and channel
- **Admin settings** — statuses, priorities, departments, categories, users, email and ticket rules
- **16 languages** with right-to-left layout for Arabic, Urdu and Persian
- **Dark mode** and a layout that works on phones

## Run locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
cd "Help DeskTicket System"
dotnet run --launch-profile http
```

Open http://localhost:5249. The SQLite database (`App_Data/helpdesk.db`) is created and filled with sample data on first start.

Development sign-in accounts are defined under `Seed` in `appsettings.Development.json`. They are for local testing only — change or remove them before deploying.

## Configuration

- **Database:** `ConnectionStrings:Default` in `appsettings.json`. The schema is created with `EnsureCreated`; after changing the data model, delete `App_Data/helpdesk.db` (or move to EF Core migrations).
- **Email:** sign in as an admin and open **Settings** to enter SMTP and IMAP details.
- **Languages:** translations live in `Resources/Lang/<code>.json`, keyed by the English text. To add a language, add a JSON file and register it in `Services/Lang.cs`.

## Project layout

```
Help DeskTicket System/
├── Data/            EF Core context and seed data
├── Models/          Entities
├── Services/        Ticket workflow, email, notifications, background jobs, localization
├── Pages/           Razor Pages (Tickets, Kb, Reports, Admin, Account, …)
├── Resources/Lang/  Translation files
└── wwwroot/         CSS, JS and client libraries
```
