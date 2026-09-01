# nativeweb.mail

The outbound e-mail seam: recipient validation, header-injection scrubbing, SMTP
configuration, and a one-connection-per-send transport. Content is not rendered here;
the app supplies it through `IEmailContentRenderer` and this library carries it to the
wire. There is no generator.

## The whole API

| Identifier | Kind | What a consumer calls it for |
| --- | --- | --- |
| `EmailContent` | record struct | One rendered e-mail: `Subject`, `TextBody`, `HtmlBody` |
| `EmailTemplates` | static class | Address validation and sanitising shared by every sender |
| `ParseRecipient` | method | Parse a recipient via MimeKit, then demand a non-empty domain |
| `Sanitize` | method | Strip control characters (tab excepted) from caller-supplied text |
| `IEmailContentRenderer` | interface | The app renders content per locale: `LoginLinkAsync`, `OneTimeCodeAsync` |
| `SmtpOptions` | options | The host binds it from a section of its choosing; `IsEnabled` and `Validate` govern use |
| `SmtpSecurityMode` | enum | `StartTls` for real submission, `None` for a loopback fake only |
| `SmtpTransport` | class | `SendAsync`: connect, authenticate, send, disconnect, once per call |

`SendAsync` takes the recipient as a string and runs it through `ParseRecipient`
itself, so a caller that renders content first and sends second needs no validation
step of its own. Authentication is skipped when `Username` is null or empty.

### The config-swap contract

An empty or whitespace `Host` means SMTP is disabled: `IsEnabled` is false, the caller
falls back to its log-based senders rather than constructing an `SmtpTransport`, and
`Validate` is a no-op because disabled configuration is always considered valid. The
log senders are expected to stay wired unconditionally, so turning SMTP off never
removes the ability to observe outbound messages in logs.

When enabled, `Validate` requires `FromAddress`, and rejects a `SecurityMode` of
`None` for any host that is not loopback (`localhost`, `127.0.0.1`, `::1`): that mode
sends codes, login links and the SMTP AUTH credentials in cleartext, so nothing but a
loopback fake may select it. `SmtpTransport` runs `Validate` in its constructor, so a
bad configuration fails at construction rather than at first send.

### Addresses

MimeKit's parser accepts a bare local part with no domain, so `ParseRecipient` does
not trust acceptance alone: it requires a non-empty domain after the final @, and
throws `ArgumentException` otherwise. A line break immediately before the @ is
accepted, because MimeKit folds it away; what is asserted of such input is that the
parsed `MailboxAddress` carries no line break, not that the input is rejected.

`Sanitize` is the second layer on top of MimeKit's own header encoding: values such
as the product name are caller-supplied and flow into both subject and bodies, so
control characters are removed before interpolation.

## Sending a login-link e-mail

```csharp
using NativeWeb.Mail;

var options = new SmtpOptions
{
    Host = "smtp.example.com",
    Port = 587,
    FromAddress = "no-reply@example.com",
    FromName = "NativeWeb",
    SecurityMode = SmtpSecurityMode.StartTls,
    TimeoutSeconds = 10,
};

var content = new EmailContent(
    "Your sign-in link",
    "Open this link to sign in: " + linkUrl,
    "<p>Open this link to sign in: " + linkUrl + "</p>");

await new SmtpTransport(options).SendAsync(toAddress, content, cancellationToken);
```

The example builds the `EmailContent` by hand to show its shape. A real sender
implements `IEmailContentRenderer` and calls `LoginLinkAsync` or `OneTimeCodeAsync`,
which resolve the recipient's stored locale and return the same record; the transport
cares only that the subject and both bodies are present.

## What this library does not do

- **No rendering.** Subjects, bodies, locales and templates belong to the app behind
  `IEmailContentRenderer`. This library is transport only.
- **No log-based fallback.** It defines the contract that says when to use one
  (`IsEnabled` false); writing the log senders is the caller's job.
- **No pooling.** Each `SendAsync` opens a fresh connection. Transactional volume
  tolerates that; pooling is a later optimisation if send rate grows.
- **No unencrypted submission off loopback.** `None` is for an in-process fake server
  during tests, and `Validate` enforces that boundary rather than trusting the caller.

## Proved by

`tests/nativeweb.mail.tests.unit`: address parsing and its
domain check, sanitising, the options contract including the loopback rule, and the
shape of a send.
