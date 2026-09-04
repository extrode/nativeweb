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
| `[FormFile]` (`FormFileAttribute`) | On a property or a parameter. Marks a file field, carrying its `MaxBytes` ceiling and an optional `Accept` set |
| `UploadedFile` | The bound file: `Name`, `ContentType`, `Length` and `OpenReadStream()`. Not `IFormFile`, so a handler never reaches back into `HttpContext` |
| `CarriedAnswers` | Signed, expiring form state carried in one hidden field between the steps of a multi-step form |
| `AddNativeWebForms()` | Registers `CarriedAnswers`. A host that skips it gets a stated failure from the container rather than a null |
| `IFormModel<TSelf>` | The seam the generated partial implements: a `BindFormAsync` that reads the declared fields |
| `MapFormPost` / `MapFormPut` / `MapFormPatch` / `MapFormDelete` | Maps a route on that verb, binding `TForm` through `TForm.BindFormAsync` |
| `MapForm` | The one implementation behind the four: takes the HTTP method as a string, for a verb they do not name |
| `FormBinding.RequireFormContentType` | Throws 415 unless the request carries a form body. The first statement of every generated binder |
| `FormBinding` `Field` | One declared field from an `IFormCollection`, or `null` when the form does not carry it |
| `FormBinding.RequireMultipart` | Throws 415 for a urlencoded post to a model with a file field. Emitted only for such a model |
| `FormBinding.LimitBody` | Installs the declared ceiling as a per-endpoint `FormOptions.MultipartBodyLengthLimit` before the read |
| `FormBinding.ReadLimitedFormAsync` | Reads the form, turning an over-limit body into 413 and a malformed one into 400 |
| `FormBinding` `File` / `RequiredFile` | One declared file field, measured against its ceiling and checked against its `Accept` set |
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
| `NWF003` | A member is neither `string?` nor a file field: text values arrive as text and may be absent, so conversion belongs in the handler |
| `NWF004` | Two members share a wire name. Compared case-insensitively, because the form collection is keyed that way, so ConfirmPassphrase collides with confirmPassphrase |
| `NWF005` | The model is a struct, static, generic, nested or file-local |
| `NWF006` | A file field cannot be bound as declared: `[FormFile]` on a member that is not an `UploadedFile`, an `UploadedFile` with no `[FormFile]`, a `MaxBytes` of zero or less, or a blank `Accept` entry |
| `NWF010` | A plain `Map*` route takes a `[Form]` model as a handler parameter. Reported by `MissingAntiforgeryAnalyzer` at the `Map*` call rather than at the model, because the same model may be bound rightly by one route and wrongly by another |

## A file field

Declare the member as `UploadedFile` (or `UploadedFile?` for an optional one) and decorate it
`[FormFile]`. The ceiling is required; the accepted content types are not.

```csharp
[Form]
public sealed partial record UploadForm(
    string? Caption,
    [FormField("picture")]
    [FormFile(MaxBytes = 1_048_576, Accept = ["image/png", "image/jpeg"])]
    UploadedFile Picture);
```

A model that has one file field changes what the generated binder does, in four ways:

| | What the emitted binder does |
| --- | --- |
| Content type | `RequireMultipart` first. A urlencoded body cannot carry a file, so binding one would report every file member missing and blame the client for a field they could not send |
| Size | `LimitBody` before the read, with the largest `MaxBytes` any file field on the model declares, because the body carries every field at once. Over it, 413 |
| Type | Each part's declared media type is compared against that field's `Accept`, parameters stripped and case-insensitively. Outside the set, 415 |
| Presence | A missing required file is 400, which is what a missing required text field gets. A part present but empty binds `null`, because a browser posts one for a file input the person left alone |

**The ceiling is enforced twice, and both are needed.** `LimitBody` aborts the read mid-stream,
which is the refusal that matters: nothing is buffered. But it cannot install a limit on a form that
has already been read, and on any antiforgery-guarded route carrying its token as a hidden field the
middleware has read the form before the endpoint runs. So `File` and `RequiredFile` measure the
bound part as well. Installing a fresh feature over a spent stream instead is a 500 for every such
route, which is what this library did until 2026-09-04.

**The client's file name is never a path.** It reaches the handler unchanged, because it is shown to
a person, and an application storing the file chooses its own name. `apps/sample/UploadStore.cs` is
the worked example.

## Carried answers

A multi-step form has to carry step one's answers into step two. `CarriedAnswers` does it in one
opaque hidden field over ASP.NET Core Data Protection's time-limited protector, so nothing is kept
on the server between the two requests and there is no session to expire, no row to clean up and
nothing to correlate.

```csharp
string carried = answers.Protect(new Dictionary<string, string> { ["role"] = form.Role }, TimeSpan.FromMinutes(20));

// next request
if (!answers.TryUnprotect(form.Carried, out IReadOnlyDictionary<string, string> read, out CarriedAnswersFailure why))
{
    // why is Absent, Altered or Expired -- three different things to say
}
```

`Expired` is separate from `Altered` on purpose: "this took too long, start again" is an instruction
and "this was altered" is not. The payload is length-prefixed key/value pairs rather than JSON, so
no reflection-based serializer is involved and the AOT publish stays clean.

It comes from `AddNativeWebForms()` and is a singleton. `IDataProtectionProvider` is already in the
container of every NativeWeb host (`NativeWebPipeline` calls `AddDataProtection().PersistKeysToFileSystem`),
so key rotation and cross-instance validity come with the host rather than needing a key of their own.

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
- **No storage.** A bound `UploadedFile` is a stream and three pieces of metadata. Where an upload
  goes, under what name, and whether it is served back are the application's, because every one of
  those is a decision this library would be guessing at.
- No token is issued or checked here beyond requiring a valid one: antiforgery is the middleware's,
  wired by the host, and this library only marks the routes it must not skip.

## Proved by

`tests/nativeweb.forms.tests.unit`: `FormBindingTests.cs`, `FormGeneratorTests.cs`,
`FormFileBindingTests.cs`, `FormFileGenerationTests.cs`, `CarriedAnswersTests.cs`, `MapFormTests.cs`,
`RequiresAntiforgeryTests.cs` and `MissingAntiforgeryAnalyzerTests.cs`, one file per public unit
above.

`tests/nativeweb.forms.tests.integration`: the binder over a real pipeline -- what each refusal
answers, and the token-as-a-field ordering the unit tier cannot construct.

`tests/nativeweb.forms.tests.e2e`: a browser completing `apps/sample`'s three public forms against
the published NativeAOT binary.
