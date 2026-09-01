# nativeweb.forms

Declarative HTML form binding for NativeWeb. Declare a partial record, decorate it `[Form]`, and a
generator writes the binder; `MapFormPost` maps the route with both guards attached by construction.

This project is the **runtime** half: the attributes, the `IFormModel<TSelf>` seam, the `FormBinding`
helpers and the route mapping. The **build-time** half is `libs/nativeweb.forms.generator`, which gets
no README of its own because a consumer never calls it, only compiles with it. A consumer references
both:

```xml
<ProjectReference Include="..\..\libs\nativeweb.forms\nativeweb.forms.csproj" />
<ProjectReference Include="..\..\libs\nativeweb.forms.generator\nativeweb.forms.generator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

## The whole API

| Member | What it is |
| --- | --- |
| `[Form]` (`FormAttribute`) | On a class. Marks a partial record as a form model, so the generator emits its binder |
| `[FormField("name")]` (`FormFieldAttribute`) | On a property or a parameter. The wire name when it differs from the member name |
| `IFormModel<TSelf>` | The seam the generated partial implements: a `BindFormAsync` that reads the declared fields |
| `MapFormPost` / `MapFormPut` / `MapFormPatch` / `MapFormDelete` | Maps a route on that verb, binding `TForm` through `TForm.BindFormAsync` |
| `MapForm` | The one implementation behind the four: takes the HTTP method as a string, for a verb they do not name |
| `FormBinding.RequireFormContentType` | Throws 415 unless the request carries a form body. The first statement of every generated binder |
| `FormBinding` `Field` | One declared field from an `IFormCollection`, or `null` when the form does not carry it |
| `RequiresAntiforgery` / `RequireFormAntiforgery` | Antiforgery metadata, and the extension that attaches it. The mapping calls do this themselves |

The handler is a `Func<TForm, HttpContext, Task<IResult>>` with
`where TForm : class, IFormModel<TForm>`, and the route is a `RequestDelegate`. Both halves of that
shape are forced rather than chosen: the `Map*` overloads taking a `Delegate` carry
`RequiresUnreferencedCode` and `RequiresDynamicCode`, and a `RequestDelegate` leaves the Request
Delegate Generator nothing to infer. The four verbs sit one call deep over `MapForm`, so the guards
cannot come off one of them.

`MapForm` is public and unconstrained, so a form can be mapped on any verb, with one caveat: the
antiforgery middleware validates only unsafe methods, so on a GET the metadata it attaches is inert
and only the 415 applies.

## A worked example

```csharp
using NativeWeb.Forms;

[Form]
public sealed partial record Register(
    string? FirstName,
    string? Passphrase,
    string? ConfirmPassphrase,
    [property: FormField("g-recaptcha-response")] string? Recaptcha);

// Program.cs, with UseAntiforgery() in the pipeline
app.MapFormPost<Register>("/auth/register", async (form, context) =>
{
    // form.FirstName bound the posted firstName; Recaptcha bound g-recaptcha-response.
    // Absent is null, present-but-blank is "". Convert and validate here.
    return Results.Ok();
});
```

A record rather than a class because binding produces a value the handler should not be able to
mutate back; partial because the binder is emitted onto the same type.

## Two guards, and where each lives

**Antiforgery, attached by the mapping call.** A route whose model binds itself carries no
antiforgery metadata of its own, so `UseAntiforgery()` skips the endpoint and a POST with no token
at all is answered 200 OK, with nothing logged. `MapForm` attaches
`RequiresAntiforgery.Instance` itself, so there is nothing to forget. `RequireFormAntiforgery` stays
public for a route that binds a form some other way, and for reading: a guard visible in the route
table is easier to review than one applied invisibly.

**The content-type check, inside the binder.** Argument binding runs upstream of the entire
endpoint-filter pipeline, so a guard written as an `AddEndpointFilter` cannot protect the read: by
the time it runs, the form has already been read. The generated binder calls
`FormBinding.RequireFormContentType` first, which throws `BadHttpRequestException` at 415. Without
it, a JSON POST surfaces as a 500 from a bare `InvalidOperationException` in `FormFeature`: a server
error for what is a client's malformed request.

The route also carries `AcceptsMetadata` for the two form content types, and `ConsumesMatcherPolicy`
reads it during routing, so a JSON POST is answered 415 before `UseAntiforgery()` runs rather than
400 for a missing token. A request carrying no content type at all still reaches the binder, because
the policy only rejects a content type it can see; its 415 comes from
`FormBinding.RequireFormContentType`.

Binding semantics, both from how a form arrives: absent is `null` and present-but-blank is `""`,
preserved rather than flattened so a caller that later wants the distinction can still reach it; and
a field the request repeats arrives joined by commas, because that is what `StringValues.ToString()`
does, so a=1&a=2 binds "1,2". A handler treating a bound value as one user-supplied token should
know the client chooses how many it sends.

## The generator

`libs/nativeweb.forms.generator` ships `FormGenerator`, an incremental generator over `[Form]`. For
each usable model it emits a partial of the same type implementing `IFormModel<TSelf>`, whose
`BindFormAsync` checks the content type first, reads the form, and fills one constructor parameter
per `FormBinding.Field` call. Only the declared members are read, so a field the model does not name
cannot reach the handler: the declaration is the whole allowlist, enforced by the compiler rather
than by a runtime filter someone has to remember to apply.

The default wire name is the member name with its first character lowercased (FirstName binds
firstName), which matches what hand-written endpoints already post. `[FormField]` covers the cases
convention cannot reach, and both attribute targets are read, so `[property: FormField("x")]` on a
record's primary constructor works too. A record is the norm and a plain class is legal; a struct is
not, because `IFormModel<TSelf>` constrains `TSelf` to a reference type.

Every refusal is a build error rather than emitted code that does not compile:

| ID | Reported when |
| --- | --- |
| `NWF001` | The model is not declared partial |
| `NWF002` | The model does not have one accessible constructor to bind through. A record's copy constructor is never a candidate |
| `NWF003` | A member is not declared string? with a nullable annotation: form values arrive as text and may be absent, so conversion belongs in the handler |
| `NWF004` | Two members share a wire name. Compared case-insensitively, because the form collection is keyed that way, so ConfirmPassphrase collides with confirmPassphrase |
| `NWF005` | The model is a struct, static, generic, nested or file-local |
| `NWF010` | A plain `Map*` route takes a `[Form]` model as a handler parameter. Reported by `MissingAntiforgeryAnalyzer` at the `Map*` call rather than at the model, because the same model may be bound rightly by one route and wrongly by another |

## Why the route is written by hand

The first design had the generator emit IBindableFromHttpContext<TSelf> onto the model, so a plain
`MapPost("/x", (MyForm f) => ...)` would bind it. It did not work, and the failure was measured on
2026-08-30: the Request Delegate Generator and this generator run in the same generation pass over
the same input compilation, so RDG never sees a partial another generator added, records
`hasBindAsync: false`, and falls back to treating the model as a JSON body. Every form POST was
answered 415 by routing itself, before any code in this library ran. The fix is the shape above: the
route is a delegate that takes only `HttpContext`, nothing is inferred, and binding is an ordinary
static call. `NWF010` makes going back to a plain route a build error rather than a silent 415.

The platform's own alternatives were measured too, in brainstorm 001 §12: a complex-type
`[FromForm]` parameter is not NativeAOT-compatible, because RDG cannot statically resolve it
(`RDG003`), falls back to reflection, and trips `IL2026` and `IL3050`, which under this repo's
`TreatWarningsAsErrors` is a failed build. `[AsParameters]` compiles and then binds every form field
to null in silence. The only shape that both binds and survives an AOT publish is a static
`BindFormAsync`, and writing one by hand drops the two guards above, neither of which fails loudly.

## What this library does not do

- No validation and no conversion. Every bound member is string? by rule (`NWF003`); parsing an int,
  checking a length and comparing Passphrase to ConfirmPassphrase are handler work, where a bad
  value can be reported to the person who typed it.
- No routes are emitted. A generator's output is invisible to the Request Delegate Generator, so an
  emitted `Map*` call binds the reflection overload and fails an AOT publish. The application writes
  the route and calls `MapFormPost` itself.
- No data access. Binding produces a value; what happens to it is the application's.
- No token is issued or checked here beyond requiring a valid one: antiforgery is the middleware's,
  wired by the host, and this library only marks the routes it must not skip.

## Proved by

`tests/nativeweb.forms.tests.unit`: `FormBindingTests.cs`, `FormGeneratorTests.cs`,
`MapFormTests.cs`, `RequiresAntiforgeryTests.cs` and `MissingAntiforgeryAnalyzerTests.cs`, one file
per public unit above.
