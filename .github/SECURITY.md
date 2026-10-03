# Security policy

## Reporting a vulnerability

Report it privately through GitHub: the **Security** tab of this repository, then **Report a
vulnerability**. That opens a private advisory only the maintainers can read.

Please do not open a public issue, pull request or discussion for a suspected vulnerability.

Include what you can of: the affected library or file, the version or commit, steps to reproduce,
and what an attacker gains. A proof of concept helps; a working exploit against someone else's
deployment does not.

## Supported versions

Fixes land on `dev` and ship in the next release. Which released minors still receive fixes is set
out in [`docs/02-reference/versioning.md`](../docs/02-reference/versioning.md).

## Scope

NativeWeb is a framework. A vulnerability in an application built on it belongs to that
application's maintainers, unless the cause is in NativeWeb's own code or defaults.
