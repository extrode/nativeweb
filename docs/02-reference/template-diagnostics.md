# NWT template diagnostics

Every mistake in a `.html` template is a **build error**, never a runtime blank.
The `nativeweb.render.generator` source generator raises them as `NWT001`–`NWT016`, category
`NativeWeb.Render`, all `DiagnosticSeverity.Error` and all enabled by default. There is no
suppression story on purpose: a suppressed template error is a page that renders wrong in
production instead of failing on the build machine.

Each one carries a real file and line, so a build-log entry is clickable. Descriptors: `libs/nativeweb.render.generator/TemplateDiagnostics.cs`.
Every example below is taken from a passing fact in
`tests/nativeweb.render.tests.unit/GeneratorDiagnosticsTests.cs` — if an example here stops
triggering its diagnostic, that test file is what to trust.

## At a glance

| ID | Title | Raised when |
| --- | --- | --- |
| NWT001 | Malformed template header | The `<!--#template … -->` header is unclosed, names a reserved `@param`, or a layout declares `@layout` |
| NWT002 | Unknown template directive | A header line is neither `@layout` nor `@param` |
| NWT003 | Malformed @param | Not `@param <type> <name>` |
| NWT004 | Layout not found | `@layout` names no template file |
| NWT005 | Partial not found | `{{> path}}` names no template file |
| NWT006 | Partial arguments do not match its @param list | Missing, unknown or malformed argument; or a layout called as a partial |
| NWT007 | Slot or fill misused | Six distinct fill/slot mistakes — see below |
| NWT008 | Unbalanced template block | A block never closed, a closer with no opener, or a reserved loop variable |
| NWT009 | Duplicate template class | Two template paths compile to one class name |
| NWT010 | Parameter conflicts with layout | Same `@param` name, different type, in page and layout |
| NWT011 | Required slot not filled | Layout declares `{{slot X}}`, page has no `{{fill X}}` |
| NWT012 | Template expression rejected | An expression contains `;`, `{` or `}` |
| NWT013 | Unknown locale key | `{{t:key}}` is not in `locales/en.json`, or is not a valid key shape |
| NWT014 | Orphan locale override | A non-English catalog defines a key `en.json` does not |
| NWT015 | Malformed locale catalog | Five distinct catalog problems — see below |
| NWT016 | Interpolation outside a quoted attribute value | `{{…}}` sits inside a tag but not inside a quoted value |

## The header: NWT001, NWT002, NWT003

The optional header is an HTML comment at the top of the file:

```html
<!--#template
@layout layout/page
@param string Title
-->
```

**NWT001** covers three things, because all three make the header itself unusable:

- The comment is never closed with `-->`.
- `@param` uses a **reserved name** — `writer`, `locale`, or any C# keyword. `writer` is the
  emitted method's own parameter and `locale` is the implicit one, so either would shadow
  machinery the generated code needs.
- A file that declares `{{slot …}}` (making it a layout) **also** declares `@layout`. Layout
  chaining is not supported, and a layout gets no class of its own, so without this check the
  outer layout would silently vanish. Checked before emission, so every page using that layout
  sees it as broken rather than half-rendered.

```html
<!--#template
@param string writer
--><p>x</p>            <!-- NWT001: 'writer' is reserved -->
```

**NWT002** is the catch-all for a fourth directive: only `@layout` and `@param` exist.

```html
<!--#template
@bogus thing
--><p>x</p>            <!-- NWT002 -->
```

**NWT003** is shape alone — `@param` takes exactly a type and a name. Types are validated here
too, so `@param My Type Name` is NWT003 rather than a C# syntax error inside generated code
you did not write.

```html
<!--#template
@param OnlyOneToken
--><p>x</p>            <!-- NWT003 -->
```

## Layouts, slots and fills: NWT004, NWT007, NWT010, NWT011

**NWT004** — `@layout layout/absent` matches no template file. Paths are the template path
without extension, so `templates/layout/page.html` is `layout/page`.

**NWT007** is one ID over six conditions, all of them "the fill/slot contract is broken":

| Condition | Example |
| --- | --- |
| `{{fill X}}` has no matching slot in the layout | layout has `{{slot Main}}`, page fills `Ghost` |
| `{{fill …}}` with no `@layout` directive at all | a page that fills nothing in particular |
| `{{fill X}}` appears more than once | two `{{fill Main}}` blocks |
| `{{fill …}}` is not at the top level | a fill nested inside `{{if}}` |
| Both `{{fill Main}}` **and** top-level content | one or the other, not both |
| The layout declares no `{{slot Main}}` | page content would be silently dropped |

The last one is the reason this is an error rather than a warning: a layout with only optional
slots swallows the page body with nothing to show for it.

**NWT010** — the page and its layout both declare a `@param` of the same name with different
types. The composed template has one parameter list, so the two cannot both be right.

**NWT011** — the layout declares `{{slot Sidebar}}` and the page never fills it. Mark the slot
optional with `{{slot? Sidebar}}` if that is intended; an unfilled `{{slot? …}}` is fine.

## Partials: NWT005, NWT006

**NWT005** — `{{> shared/absent}}` matches no template file.

**NWT006** covers every way a call can disagree with the callee's `@param` list, and reports all
of them in one message rather than one per build:

- Missing a required argument.
- Passing an argument the partial does not declare.
- A malformed argument: they are `Name=expression` with **no spaces in the expression**, so
  `{{> shared/field Label="Hello world"}}` is NWT006. Compute the value in the calling C# and
  pass it as a parameter.
- Calling a **layout** as a partial. A file with slots is a layout; it cannot be invoked with
  `{{> …}}`.

## Blocks and expressions: NWT008, NWT012

**NWT008** — `{{if}}`, `{{for}}` and `{{fill}}` all open a block that must close. It fires on an
unclosed opener, on a closer with no opener (`{{/if}}` alone), and on a `{{for}}` whose loop
variable is not a valid identifier or is reserved (`{{for locale in Items}}`).

**NWT012** — a template expression may not contain `;`, `{` or `}`. Expressions are **values,
not statements**; anything with control flow or a statement separator belongs in the calling C#.
This is a hard syntactic bar rather than a parse of what the expression does, which is the point:
it cannot be argued around.

```html
<p>{{X; System.IO.File.Delete("x")}}</p>   <!-- NWT012 -->
```

## Localization: NWT013, NWT014, NWT015

`locales/en.json` is the root catalog and the fallthrough for every locale.

**NWT013** — `{{t:key}}` where the key is absent from `en.json`, or is not a valid key shape
(letters, digits, `.`, `_`, `-`).

**NWT014** — the inverse: `locales/ms.json` defines a key `en.json` does not. Overrides refine
the English root, they do not extend it, so this is almost always a typo.

**NWT015** covers the catalog files themselves:

| Condition | Example |
| --- | --- |
| `en.json` is missing entirely | no root catalog to fall through to |
| A value is not a string or a nested object | `{ "app": [1, 2] }` |
| An `_`-prefixed metadata key has an object value | `{ "_meta": { "x": "y" } }` |
| `_dir` is not `ltr` or `rtl` | `{ "_dir": "sideways" }` |
| Two locale ids collide on the generated member name | `pt-br.json` and `pt_br.json`, or `all.json` colliding with `Locales.All` |

A nested `_`-prefixed key is metadata, not a string key: `{"app": {"_note": …}}` does **not**
become `app._note` and does not trip NWT014.

## NWT016 — interpolation outside a quoted attribute value

The newest and the only one that exists purely as a security guard. `TemplateWriter` encodes
`<`, `>`, `&`, `"` and `'` — and nothing else. That set is safe in text and inside a **quoted**
attribute value, and nowhere else. A space is not encoded, so a value interpolated into an
unquoted attribute can close it and open `onfocus=…`; interpolating into the attribute-*name*
position is the same escape by a shorter route.

`TemplateWriter`'s own comment had asserted this invariant since day one and nothing enforced it.
NWT016 is what makes it checkable.

Rejected:

```html
<div class={{Value}}>x</div>
<div class = {{Value}}>x</div>
<div data-x=prefix{{Value}}>x</div>
<div {{Value}}>x</div>
<div class="a" {{Value}}>x</div>
<div title={{t:app.name}}>x</div>
```

Accepted — quote the value, single or double:

```html
<div class="{{Value}}">x</div>
```

The negative cases carry more weight than the positive ones here. A guard that fires on ordinary
markup gets deleted rather than obeyed, so `GeneratorDiagnosticsTests` pins a longer list of
shapes that must **not** raise NWT016 than of shapes that must.
