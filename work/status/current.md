# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- `feat/render-diagnostics-locations` — render-generator diagnostics currently all report at
  `Location.None`, so NWT001–NWT015 point at nothing. Carry the physical `AdditionalText` path
  through `TemplateModel`, emit real file locations, validate `@param` types up front instead of
  letting them fail as C# errors in generated code, and reject `{{expr}}` in an unquoted
  attribute value — an invariant `TemplateWriter`'s comment asserts but nothing enforces.
