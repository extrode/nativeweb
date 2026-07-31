# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **V2 (spec 003, architecture v2)** — `feat/render-engine`: `libs/nativeweb.render` runtime
  (`TemplateWriter` over IBufferWriter<byte>, `Html` raw type, IResult adapters) +
  `libs/nativeweb.render.generator` source generator (templates/**/*.html AdditionalFiles →
  static render classes, UTF-8 literals, encoded-by-default, partials/layouts/required slots,
  build-error diagnostics), AngleSharp well-formedness harness, unit tests.
