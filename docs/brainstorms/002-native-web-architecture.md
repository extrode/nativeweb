# Lean Native Web (LNW) Architecture Specification

## Vision

Lean Native Web (LNW) is a server-first architecture for ASP.NET Core that prioritizes explicit code, NativeAOT compatibility, predictable performance, and long-term maintainability.

It intentionally avoids unnecessary framework abstractions while keeping the mature parts of ASP.NET Core that are difficult or risky to replace.

The result should feel closer to Go or Rust web services than a traditional ASP.NET MVC application.

------

# Core Principles

## 1. NativeAOT First

The application must be designed around NativeAOT from the beginning.

Do not add libraries or patterns that require:

- runtime code generation
- runtime reflection
- assembly scanning
- dynamic proxies
- plugin loading

Everything should be trim-safe and compile-time friendly.

------

## 2. Server Owns the Application

The server is the source of truth.

The server owns:

- business logic
- routing
- authentication
- authorization
- validation
- HTML generation
- database access

The browser is a presentation layer.

------

## 3. HTML First

When building the application's own UI:

Prefer

HTML → Browser

instead of

JSON → JavaScript → HTML

Use JSON only for:

- public APIs
- third-party integrations
- mobile clients
- external consumers

Internal application pages should primarily exchange HTML fragments.

------

## 4. Explicit Over Magic

Avoid runtime conventions.

Avoid hidden behavior.

Prefer code that is obvious from reading the source.

Bad

- assembly scanning
- automatic registration
- reflection-based discovery
- hidden conventions

Good

- explicit object creation
- explicit endpoint registration
- explicit routing
- explicit configuration

------

## 5. Lean by Default

Every dependency must justify its existence.

Do not introduce abstraction unless it solves a demonstrated problem.

------

# Technology Stack

Backend

- .NET 10
- C# 14
- ASP.NET Core Minimal APIs
- NativeAOT
- System.Text.Json source generation

Frontend

- HTMX
- Alpine.js
- Static HTML
- Static CSS
- Optional Web Components

Database

- SQLite for development
- PostgreSQL for production

Data Access

- Dapper

Authentication

- ASP.NET Core Authentication
- JWT Bearer or Cookie Authentication

Authorization

- ASP.NET Core Authorization Policies

------

# Technologies Explicitly Avoided

Unless there is a documented reason.

- MVC
- Controllers
- Razor Pages
- Blazor
- MediatR
- AutoMapper
- Repository Pattern
- Generic Service Pattern
- Assembly Scanning
- Reflection-based serializers
- Runtime code generation
- Service Locator
- Dynamic plugins

------

# Dependency Injection

The built-in ASP.NET dependency injection container should be minimized.

Prefer direct object creation.

Example

```csharp
var database = new Database(connectionString);

var jwt = new JwtService(secret);
```

instead of

```csharp
builder.Services.AddSingleton<Database>();
```

The DI container should only be used when required by framework integrations, such as authentication or other middleware that depends on service registration.

------

# Routing

Use Minimal APIs exclusively.

Endpoints should be organized by feature.

Example

```
Endpoints/

    Home.cs

    Auth.cs

    Todo.cs

    Admin.cs
```

Each feature owns its routes.

------

# Endpoint Philosophy

Each endpoint should:

- receive request
- validate input
- execute business logic
- return TypedResults

Avoid unnecessary service layers.

Simple request flow

Endpoint

↓

Database

↓

HTML

Not

Controller

↓

Service

↓

Repository

↓

Mapper

↓

DTO

↓

Database

------

# HTML Rendering

Return HTML for application pages.

Return JSON only when external clients require it.

HTMX should consume HTML fragments.

------

# HTMX Responsibilities

HTMX owns:

- navigation
- CRUD interactions
- pagination
- filtering
- searching
- infinite scrolling
- partial page updates

HTMX should communicate directly with Minimal API endpoints.

------

# Alpine Responsibilities

Alpine owns only local UI state.

Examples

- modal visibility
- dropdowns
- tabs
- accordions
- inline editing
- client-side toggles

Alpine should not become an application framework.

------

# Web Components

Use Web Components only for reusable UI widgets.

Good candidates

- Navbar
- Sidebar
- Dialog
- Toast
- Avatar
- Loading Spinner
- Theme Toggle

Do not create business-domain components such as:

- CustomerList
- InvoicePage
- OrdersScreen

Those belong on the server.

------

# Static Assets

Use wwwroot.

```
wwwroot/

    css/

    js/

    images/

    components/

    partials/
```

No build pipeline unless absolutely necessary.

No npm dependency chain for simple applications.

------

# JavaScript Philosophy

Keep JavaScript minimal.

Most interactivity should come from:

- HTMX
- Alpine

Avoid SPA architecture.

------

# Authentication

Always use ASP.NET Core Authentication.

Never manually implement:

- JWT parsing
- signature verification
- cookie encryption
- claims validation

Keep Microsoft's security implementation.

------

# Authorization

Use Authorization Policies.

Prefer

```csharp
.RequireAuthorization("Admin")
```

over manual claim inspection.

------

# Serialization

Always use source-generated System.Text.Json.

Reflection serialization is prohibited.

------

# Database Layer

Prefer Dapper.

The database layer should remain thin.

Avoid generic repositories.

Example

```
TodoRepository.cs
```

is acceptable.

```
IRepository<T>
```

is not.

------

# Configuration

Use strongly typed configuration objects.

Load configuration once.

Avoid injecting IConfiguration throughout the application.

------

# Error Handling

Use centralized middleware.

Return:

- ProblemDetails
- appropriate HTTP status codes
- user-friendly HTML pages

Avoid exception-driven business logic.

------

# Logging

Prefer built-in ILogger only when needed.

Avoid large logging frameworks unless required.

------

# Project Structure

```
LeanNativeWeb/

│
├── Program.cs
├── LeanNativeWeb.csproj
├── appsettings.json
│
├── Auth/
│
├── Data/
│
├── Endpoints/
│
├── Models/
│
├── Serialization/
│
├── Utilities/
│
└── wwwroot/
```

Organize by feature, not by technical layer.

------

# Performance Goals

Prioritize

- fast startup
- low allocations
- low memory usage
- NativeAOT compatibility
- trim safety
- explicit object graphs
- minimal dependencies

Avoid premature micro-optimizations.

Measure before optimizing.

------

# Design Goals

The finished application should feel:

- explicit
- predictable
- boring
- easy to debug
- easy to profile
- easy to deploy
- NativeAOT-friendly
- browser-native

------

# Guiding Philosophy

Lean Native Web embraces modern browser capabilities instead of replacing them.

It uses:

- HTML as the application protocol
- HTMX for hypermedia interactions
- Alpine.js for lightweight client-side state
- ASP.NET Core Minimal APIs for routing
- NativeAOT for performance
- explicit code instead of runtime magic

The architecture intentionally rejects unnecessary abstraction in favor of simple, composable building blocks that are easy to understand, maintain, and optimize.