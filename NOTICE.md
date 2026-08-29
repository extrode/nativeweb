# NOTICE

This file is required by **ISL-NETC 1.1 §3.1(c)**: any copy or substantial portion of this
software, and any derivative work distributed or made available over a network, must retain it.

```
Product:                NativeWeb
Licensor:               Extrode LLC
Copyright:              Copyright (c) 2026 Extrode LLC. All rights reserved.
License:                Islamic Software License - Network Copyleft (ISL-NETC), Version 1.1
SPDX identifier:        LicenseRef-ISL-NETC-1.1
Governing jurisdiction: Commonwealth of Virginia
License text:           ./LICENSE.md   ·   https://islamiclicense.org/isl-netc/1.1/LICENSE.md
```

The URL is version-pinned on purpose. `https://islamiclicense.org/isl-netc/` serves whichever
text is current, so it named 1.0 when this file was first written and names 1.1 now; a notice
that drifts against the `LICENSE.md` beside it is worse than no URL at all.

## Who the Licensor is

**`LICENSE.md` §1.2 names Extrode LLC.** No override is needed, and none is made here.

ISL-NETC 1.1 §1.2 reads `[LICENSOR]` — a bracketed field the party applying the license fills in.
Its *Notice on Reproducing This License* says in terms that completing those fields "is not a
modification of this text for the purposes of this notice", and that a copy so completed "remains
an unmodified reproduction of this License, and may be published, distributed, and applied to a
work under its ISL name." So `LICENSE.md` is an unmodified 1.1 reproduction with one field filled;
it differs from the published text at that line and nowhere else.

Until 2026-08-30 this repository carried 1.0, whose §1.2 hardcoded Ikhbat Foundation because that
text was drafted as the Foundation's own license. This section then existed to override it, on the
authority of the ISL-CLA §1.2 note. Version 1.1 exists to retire that workaround, and retiring it
is why the re-sync happened.

## Two copyrights, not one

| what | held by |
|---|---|
| the **text of ISL-NETC 1.1** in `LICENSE.md` | Ikhbat Foundation (ikhbat.org), reproduced under its own reproduction notice with §1.2 completed |
| the **software** the license is applied to | Extrode LLC |

Ikhbat Foundation is not the author, publisher or endorser of this software. ISL-NETC §12 (No
Endorsement and Trademarks) applies: applying the license grants no right to use the Foundation's
or Extrode's names beyond describing the origin of the work and reproducing this file.

## What binds an operator

ISL-NETC is network copyleft. Two obligations reach further than an ordinary permissive license,
and neither is waived here:

- **§3.6 Remote Network Interaction.** Running a modified version of this software as a hosted or
  SaaS offering obliges the operator to offer those users the Corresponding Source of *that exact
  modified version*, at no charge, under ISL-NETC. Never distributing a copy does not avoid it.
- **§§4 and 5 Ethical Use Restrictions.** These bind every recipient, every derivative work and
  every user of a network-operated modified version, and §3.2 forbids relicensing under terms
  that remove, narrow or waive them downstream.

## Third-party components

The vendored browser libraries under `libs/nativeweb.assets/` (htmx, Alpine.js) carry their own
upstream licenses and are not covered by ISL-NETC. **JauntyQ** is used from a local checkout under
a separate license that permits self-hosted-SaaS use of the generated output only; JauntyQ source
and binaries are not redistributed with this software, and this software's binary is not shipped
to third parties.

## Standing caveat

ISL-NETC 1.1 states of itself that it "is provided as a legal template and should be reviewed by
a qualified attorney before use in production," and that its drafters assume no liability for its
legal sufficiency in any jurisdiction. No attorney has reviewed it for Extrode LLC. It is not an
OSI-approved open-source license and does not claim to be one.

Contribution terms are in [`CONTRIBUTING.md`](./CONTRIBUTING.md).
