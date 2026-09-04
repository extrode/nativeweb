# 027 — Sample forms examples: contact, upload, two-step

Status: draft
Created: 2026-09-04

## Problem

`libs/nativeweb.forms` shipped in spec 014 and nothing in the tree exercises it past the
scaffolded admin's own edit screens. Someone adopting the layer has a README and a generated
`BindFormAsync`, and no worked example of the three form shapes every real application needs: a
public form that sends a message, a form that accepts a file, and a form too long for one screen.

Two of those cannot be written today. The generated binder has no file-valued field, so an upload
has to reach around the binder into `HttpContext.Request.Form.Files` by hand — which is the exact
hand-rolling the library exists to remove, and the version of it that skips the binder's
content-type guard. And nothing in the framework carries part-finished form state from one request
to the next, so a two-step form is left to each application to invent.

The cost is measured elsewhere in this repo: `epass.io` hand-ported five capabilities
`libs/nativeweb.web` already shipped because nothing showed it how. A form library with no worked
upload is the same gap one layer down.

## Goals

- A file-valued field is declared on a form model and bound by the generated binder, with the
  same compile-time guarantees the library's other fields have.
- The framework offers one way to carry a part-finished form between requests, needing no server
  state and no new backing store.
- `apps/sample` serves three public forms that a person can complete in a browser, and each is
  proved through the real entry point rather than only at the library tier.
- The mail library is exercised by something other than its own tests for the first time.

## Non-goals

- **No product.** These are reference examples on the framework, per the scope boundary in
  `AGENTS.md`. Nothing here becomes a contact-management feature, a media library, or a
  user-facing upload product.
- No object storage, no image processing, no thumbnailing, no virus scanning, no resumable or
  chunked upload.
- No general session state, no `TempData` equivalent, no server-side wizard store. The two-step
  form carries its own state or it is not in this spec.
- No new admin screens beyond what is needed to see that a submission arrived.
- No change to the scaffolded admin's existing forms.

## User stories

- As someone adopting NativeWeb, I want a form model with a file field to bind like every other
  field, so that accepting an upload is a declaration rather than a handler that reads the request
  by hand.
- As someone adopting NativeWeb, I want a worked two-step form, so that I do not invent a wizard
  and get its state handling wrong.
- As a visitor to the sample, I want to send a message and get a confirmation, so that the form
  visibly did something.
- As an operator, I want an upload that cannot fill the disk or accept an arbitrary content type,
  so that a public form is not a liability.

## Requirements

- R1 A form model may declare a file-valued field, and the generated binder populates it from a
  multipart request.
- R2 A file-valued field bound from a request that carries no file is a stated outcome, not an
  unhandled failure: an optional field binds empty and a required one is rejected the way a
  missing required text field is.
- R3 A file-valued field on a form posted as `application/x-www-form-urlencoded` is rejected with
  a client error, not a server error.
- R4 A maximum accepted size and a set of accepted content types are declared alongside the
  field. A request exceeding the size limit is refused before the body is read to completion. A
  request carrying a content type outside the declared set is refused after the form is read and
  before any byte of it is written to storage.

  **Amended 2026-09-04, during planning.** R4 originally required the early refusal for both.
  `IFormCollection` exposes a part's content type only once the whole form has been read, so
  refusing earlier needs a hand-written `MultipartReader` loop in place of `ReadFormAsync` --
  a different and much larger design. The size half is unchanged and is a real mid-read abort.
- R5 Declaring a file-valued field in a way the binder cannot honour is a build error carrying a
  diagnostic id in the library's existing `NWF` series, not a runtime failure.
- R6 The framework offers a way to carry a set of form answers from one request to the next such
  that the answers cannot be read or altered by the client, and that requires no server-side
  state.
- R7 Answers carried between requests expire, and a submission presenting expired or tampered
  answers is refused with a stated status and a message a person can act on.
- R8 `apps/sample` serves a contact form that accepts a name, an address and a message, and a
  submission sends mail through `libs/nativeweb.mail`.
- R9 A contact submission that cannot be delivered does not answer the visitor with a server
  error, and the failure is recorded where an operator can find it.
- R10 The contact form is protected against automated submission by the framework's existing bot
  traps and antiforgery, with no new mechanism introduced.
- R11 `apps/sample` serves an upload form that accepts one file, stores it under a configured
  directory, and shows the visitor what was stored.
- R12 The directory an upload is stored under is configuration, with a stated default and a
  stated failure mode when it is absent or unwritable, following the pattern the framework's
  existing configuration keys use.
- R13 A stored upload is served back under a URL that cannot be used to read a file outside the
  configured directory, and a name supplied by the client never becomes a path.
- R14 `apps/sample` serves a two-step form where the first step's answers are carried into the
  second and both are submitted together.
- R15 Navigating back to the first step of the two-step form, or submitting the second step
  twice, leaves the sample in a stated condition rather than an accidental one.
- R16 Each of the three forms is reachable from the sample's public navigation.
- R17 Every new library capability has unit and integration coverage in its own library's tier,
  and each of the three sample forms is completed end to end in a browser.
- R18 The forms library's README documents the file field and the carried-state helper, and the
  changelog records both under Unreleased.

## Acceptance criteria

- Given a form model with a required file field, when a multipart request carrying a file is
  posted, then the bound model exposes the file's name, content type, length and content, and the
  handler reads no part of `HttpContext.Request` itself (R1).
- Given the same model, when a multipart request carrying no file is posted, then the request is
  refused with the same status and shape as a missing required text field (R2).
- Given the same model, when the request is `application/x-www-form-urlencoded`, then the response
  is 415 (R3).
- Given a field declared with a 1 MB limit, when a 2 MB file is posted, then the response is 413
  and the whole body was not buffered (R4).
- Given a field declared with a content-type set that excludes `application/zip`, when a zip is
  posted, then the response is a client error naming what is accepted, and nothing was written to
  storage (R4).
- Given a build where a file field is declared on a model the binder cannot honour, then the build
  fails with an `NWF` diagnostic whose message names the declaration and what to do (R5).
- Given first-step answers carried to the second step, when the carried value is altered by one
  byte, then the submission is refused and no part of it is persisted or sent (R6, R7).
- Given carried answers older than their lifetime, when the second step is submitted, then the
  response tells the visitor to start again rather than failing opaquely (R7).
- Given a valid contact submission, when it is posted, then mail is sent through the mail library
  and the visitor sees a confirmation naming what was sent (R8).
- Given an SMTP host that refuses the connection, when a contact form is submitted, then the
  visitor does not see a 500 and the failure is recorded (R9).
- Given a submission with the bot trap tripped or the antiforgery token absent, then it is refused
  by the mechanisms already in the framework (R10).
- Given a valid upload, when it is posted, then a file exists under the configured directory and
  the visitor is shown its stored identity (R11).
- Given a file name of `../../etc/passwd`, when it is uploaded, then nothing is written outside the
  configured directory (R13).
- Given a configured directory that does not exist or cannot be written, then the failure mode is
  the one the spec states, at the moment the spec states it (R12).
- Given the second step submitted twice, then the second submission leaves the stated condition
  and not a duplicate side effect (R15).
- Given the published NativeAOT sample, when a browser completes each of the three forms, then
  each reaches its confirmation, and the publish raises no trim or AOT warning (R17).

## Open questions

- None. The four scope decisions were taken by the user on 2026-09-04: the spec adds framework
  capability as well as the sample examples; an upload is stored on the filesystem under a
  configured directory; the two-step form carries signed hidden fields rather than server state;
  and the contact form sends through `libs/nativeweb.mail`.
