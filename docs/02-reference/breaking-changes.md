# Breaking changes

Every change that makes a consumer's existing code stop compiling or stop behaving as it did,
newest first. A consumer reads this file top-down from wherever they last upgraded and stops
when they reach an entry they have already taken.

**Entries are dated.** Every entry below predates the first release, so none carries a version.
Entries from the first `v*` tag onward name the version they first shipped in as well.

**What a version promises is in [`versioning.md`](versioning.md)** — what each component means,
what counts as breaking, how long a released minor is supported, and the deprecation path a
library takes before it is removed.

**A breaking change is not a defect.** This repository would rather break a seam than carry a
shim that makes the wrong shape survivable, because a shim outlives the reason for it and the
next reader cannot tell which of the two overloads is the one to write against. What is owed
instead is this file: what changed, why, and the before/after a consumer edits by.

---

## 2026-09-05 — four shared templates are delivered by the framework, not vendored

**Seam:** `NativeWeb.Web`'s MSBuild contribution — `libs/nativeweb.web/build/NativeWeb.Web.targets`.
**Affects:** every product carrying its own `templates/shared/antiforgery.html`,
`bot-traps.html`, `banner-ok.html` or `banner-error.html`. A product carrying none of them
takes this change by rebuilding and changes nothing.

These four render framework markup from framework code: `Chrome.For` fills the antiforgery
input and issues the bot-trap canary, and `Flash.Take` feeds the two banners. As per-product
copies they were a fix that could not ship. `bot-traps.html` was the sharp end — it spells five
`BotTraps` field names as literal text, and a renamed constant left every vendored copy
rendering fields the inspector no longer reads, with no compile error, no test failure and no
runtime error. Bot detection just stopped.

They now live at `libs/nativeweb.web/templates/shared/` and are contributed as `AdditionalFiles`
by `NativeWeb.Web.targets`. The logical names are unchanged — the render generator keys a
template's name on the part after the last `/templates/` segment of its physical path — so
**no call site changes**: `{{> shared/bot-traps Canary=BotCanary}}` resolves as it always did.

**Delete your copy.** That is the whole upgrade:

```bash
rm templates/shared/antiforgery.html templates/shared/bot-traps.html
rm templates/shared/banner-ok.html templates/shared/banner-error.html
```

Leaving one in place is a build error naming the file, not a silent last-wins — the generator
keys its model dictionary on the logical path, so the second file to arrive replaced
the first, and which one that was depended on MSBuild item order.

Two escape hatches, if deleting is not what you want:

```xml
<!-- keep your own copy of one, and say so -->
<ItemGroup>
  <NativeWebTemplateOverride Include="shared\bot-traps.html" />
</ItemGroup>

<!-- take none of them -->
<PropertyGroup>
  <NativeWebImportTemplates>false</NativeWebImportTemplates>
</PropertyGroup>
```

An override logs a message naming what it replaced. Turning the import off entirely makes a call
site to a framework template fail at compile time with NWT005, which is the right failure.

---

## 2026-09-01 — `IScaffoldStore<T>.ListAsync` takes an ordering and a cursor

**Seam:** `NativeWeb.Scaffold.IScaffoldStore<T>` — `libs/nativeweb.scaffold/IScaffoldStore.cs`.
**Affects:** every hand-written store. Nothing generated changes: the generated endpoints, forms
and routes are emitted against the new signature and a consumer regenerates by rebuilding.

Offset paging is gone from the seam and from the generated list. A scaffolded list could not be
ordered at all, and it paged by `skip`/`take`, which means a row inserted ahead of a reader
pushes one row from the page they are on to the page they are about to request and they never
see it.

```csharp
// before
ValueTask<IReadOnlyList<T>> ListAsync(
    string? search, IReadOnlyList<string> searchFields, int skip, int take, CancellationToken ct);

// after
ValueTask<IReadOnlyList<T>> ListAsync(
    string? search,
    IReadOnlyList<string> searchFields,
    ScaffoldSortSpec? sort,
    IReadOnlyList<string> sortFields,
    ScaffoldCursor? after,
    int take,
    CancellationToken ct);
```

`CountAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync` and `DeleteAsync` are unchanged.

### What a consumer does

1. **Take the new parameters.** `sort` is a property name the generated endpoint has already
   checked against the model's `[ScaffoldSort]` set, so a store does not re-validate it; a name it
   does not recognise falls back to the key order rather than throwing, because the only way that
   happens is a hand-written caller. `sortFields` is that allow-list, for a store that wants it.
2. **Page from `after` instead of `skip`.** `after` is null for the first page. Otherwise it
   carries the boundary row's sort value (as text, or null for SQL `NULL`), its key, and whether
   the reader is travelling backwards. A backward page returns the rows *before* the boundary,
   still in display order — mirror the comparison and the `ORDER BY`, then reverse the rows.
3. **End every order with the key.** The framework cannot enforce this and every guarantee it
   makes depends on it: a page boundary inside a block of equal sort values has no single answer
   without a tiebreaker, and the rows there repeat.
4. **Fix where NULLs go on a nullable sorted column**, in the query rather than by default.
   `(author, id) > (@a, @id)` evaluates to `NULL` — not true — for an author-less row, so `WHERE`
   drops it and every page after the first silently omits every one of them. The reference app's
   `db/sample/queries/Articles/ListByAuthorAsc.sql` carries the widened predicate and the whole
   explanation. **This is invisible on a table small enough to fit one page.**
5. **Mark sortable columns.** A column with no `[ScaffoldSort]` renders as an inert heading, which
   is how a reader tells what can order the list. The key is always sortable.

A store with no ordering to give can satisfy the seam by ignoring `sort` and paging on the key
alone; it will be correct, just not sortable.

Callers of `ListAsync` that are not stores — anything that lists rows for its own purposes — pass
`(null, [], null, [], null, take, ct)`.

---

## 2026-08-31 — the identity-provider sign-in library removed

**Seam:** the identity-provider-specific sign-in library (`libs/nativeweb.signin.<provider>`) and
its `Add<Provider>(...)` builder on top of `AddNativeWebSignIn`.
**Commit:** `f743436`. **Decision:**
[007](../decisions/2026-08-31-007-idp-leaves-nativeweb.md).
**Affects:** anything registering that provider through NativeWeb.

`libs/nativeweb.signin` — the generic OIDC/OAuth2 **client**, its session handler, bearer
introspection filter and store seams — stays and is unchanged. What went is the provider-specific
client, DTOs and registration built on top of it. A consumer configures the same flow through the
generic library, supplying the provider's endpoints from configuration instead of getting them
from a compiled-in provider class.

This is the scope boundary the repository holds: Django ships `contrib.auth` and leaves the
OAuth2/OIDC server to a third-party package. A sign-in **client** is framework; a named
provider's integration is a product's.

---

## 2026-08-31 — `libs/nativeweb.auth` and `libs/nativeweb.data` removed

**Commit:** `f743436`. **Decision:**
[007](../decisions/2026-08-31-007-idp-leaves-nativeweb.md).
**Affects:** anything referencing `TokenIssuer`, `SilentConsent`, `RealmResolver`, `SsoModes`,
`WebAuthnPolicy`, `ClientAuthenticator`, or the generated accessors over the identity schema.

Removed together with `apps/web` (the identity provider), its `db/` migrations of users, realms
and refresh tokens, and seven test projects. **None of it is ported and there is no replacement
here.** The identity provider is a separate product; decision 007 records why.

The pieces of that tree that *are* framework stayed: password and token hashing, opaque token
generation, bot traps and the data protector are in `libs/nativeweb.security`.

A consumer that needs an identity provider uses one. A consumer that needs users and passwords
builds them on the primitives, which is the `contrib.auth` side of the line.
