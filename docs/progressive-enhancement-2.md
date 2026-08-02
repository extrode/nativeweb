# Lean Native Web: Additional Conventions and Lightweight Framework Rules

This document extends the previous Lean Native Web (LNW) handoff.

The goal is to add developer ergonomics without turning LNW into a heavy framework.

The guiding rule:

> Every feature must remove more code than it adds.

LNW must remain small, explicit, NativeAOT-friendly, and understandable by a single developer.

---

# Core Philosophy Additions

## HTML First

HTML is the primary application contract.

The server produces HTML.

The browser consumes HTML.

JavaScript enhances behavior but must not become the application runtime.

---

## Progressive Enhancement Is Mandatory

Every feature must work without JavaScript.

JavaScript provides:

* faster navigation
* partial updates
* better interaction
* smoother UX

JavaScript must not be required for:

* login
* logout
* CRUD
* forms
* navigation
* search
* pagination

---

# HTML Normalization Layer

LNW includes a tiny browser-side enhancement script.

Its purpose:

* Convert semantic HTML conventions into HTMX behavior.
* Leave existing HTMX untouched.
* Avoid custom frontend frameworks.

It is not:

* a renderer
* a state manager
* a component framework
* a replacement for HTMX

---

# Normalization Rules

## Existing HTMX Wins

If developers explicitly write:

```html
<form hx-post="/todos">
```

do nothing.

Explicit HTMX always takes priority.

---

## Semantic Attributes

Prefer:

```html
<form
    action="/todos"
    method="post">
```

over:

```html
<form
    hx-post="/todos">
```

The semantic version remains functional without JavaScript.

---

# HTTP Method Enhancement

HTML forms only support GET and POST.

LNW supports semantic method hints.

Example:

```html
<form
    action="/todos/5"
    method="post"
    data-lnw-method="delete">
```

Without JavaScript:

* Normal POST submission.
* Server handles fallback route.

With JavaScript:

Becomes:

```html
<form
    hx-delete="/todos/5">
```

Supported methods:

* PUT
* PATCH
* DELETE

---

# Server Endpoint Rule

Enhanced and non-enhanced requests must execute the same business logic.

Example:

Canonical:

```
DELETE /todos/5
```

Fallback:

```
POST /todos/5/delete
```

Both call:

```
DeleteTodo(id)
```

Transport differs.

Behavior does not.

---

# Attribute Mapping

LNW may normalize common attributes.

Examples:

```html
target="#content"
```

becomes:

```html
hx-target="#content"
```

---

```html
swap="outerHTML"
```

becomes:

```html
hx-swap="outerHTML"
```

---

```html
trigger="revealed"
```

becomes:

```html
hx-trigger="revealed"
```

---

```html
confirm="Delete?"
```

becomes:

```html
hx-confirm="Delete?"
```

---

Only normalize when the HTMX equivalent does not already exist.

---

# Convention-Based UX

The normalization layer may provide sensible defaults.

Examples:

## Busy States

Automatically:

* disable submitting buttons
* show loading indicators
* restore state after completion

Avoid requiring developers to repeat:

```html
hx-indicator
```

everywhere.

---

## Error Handling

Provide a consistent error target.

Example:

```html
<div id="errors"></div>
```

Failed requests should automatically render validation errors there.

---

## Flash Messages

Provide a standard server response mechanism.

Example:

```text
Success:
Todo created.

Error:
Unable to save todo.
```

Normal requests:

* redirect with flash.

HTMX requests:

* return fragment or trigger.

Same application behavior.

---

# Browser-Native First

Before adding JavaScript or libraries, check if the browser already supports the feature.

Prefer:

* dialog
* details
* summary
* popover
* template
* native validation
* lazy loading
* view transitions

Do not build replacements for browser capabilities.

---

# Resource-Oriented Structure

Organize around resources.

Preferred:

```
Features/

    Todo/

        Endpoints.cs
        Model.cs
        Views/
            list.html
            item.html
            form.html
```

Avoid:

```
Controllers/
Services/
Repositories/
DTOs/
```

unless there is a real need.

---

# HTML Fragments as First-Class Outputs

Fragments are not an afterthought.

Endpoints should naturally support:

Full page:

```
GET /todos
```

Fragment:

```
GET /todos:list
```

or equivalent content negotiation using HTMX headers.

The same rendering logic should support:

* full page responses
* partial HTML responses

---

# Lightweight HTML Rendering

LNW may provide one small HTML rendering abstraction.

Requirements:

* No custom template language.
* No Razor replacement.
* No complicated pipeline.

Purpose:

```csharp
return Html.Page("todos/index", model);
```

and

```csharp
return Html.Fragment("todo/item", todo);
```

The renderer decides:

* full layout
* fragment only
* appropriate response format

---

# Route Generation

Optional lightweight named routes.

Example:

```html
<a route="Todo.Details" id="5">
```

becomes:

```html
<a href="/todos/5">
```

Purpose:

* avoid hardcoded URLs
* improve refactoring

Do not create a complex routing DSL.

---

# Request Context

Avoid repeatedly passing HTTP concerns everywhere.

A lightweight immutable request context is acceptable.

Example:

```csharp
RequestContext

- Current user
- Cancellation token
- Is HTMX request
- Authentication status
```

Do not turn this into a service locator.

---

# Strongly Typed IDs

Prefer value objects for important identifiers.

Example:

```csharp
TodoId
UserId
OrderId
```

instead of:

```csharp
int id
```

Benefits:

* prevents accidental mixing
* improves readability
* no runtime overhead

---

# Built-In Defaults

Every LNW application should have sensible defaults for:

## Error Pages

Provide good:

* 404
* 401
* 403
* 500

pages.

---

## Health Endpoints

Standard:

```
/health
/version
```

No setup required.

---

## Asset Versioning

Static assets should support cache busting.

Developer writes:

```html
<script src="/js/app.js">
```

Publishing adds versioning.

No frontend build pipeline required.

---

## Content Security Policy

Automatically support CSP nonces.

Developers should not manually manage script security.

---

# Keep the Framework Small

Do not add:

* custom ORM
* custom migration system
* custom authentication system
* custom DI container
* custom template language
* custom state management
* custom JavaScript framework
* code generators everywhere

Use existing proven solutions where appropriate.

---

# Final LNW Principle

Lean Native Web should feel like:

```
HTML
+
HTTP
+
ASP.NET Core
+
HTMX
+
Small enhancements
```

not:

```
A new frontend framework
+
A new backend framework
+
A new language
```

The framework should disappear.

Developers should mostly write normal HTML and C# with a small set of conventions that improve productivity without hiding how the application works.
