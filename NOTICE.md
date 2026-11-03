# NOTICE

**ISL-NETC 1.2 §3.1(c) requires this file to be kept.** Any copy or substantial portion of this
software, and any derivative work distributed or made available over a network, must retain it.

```
Product:                NativeWeb
Licensor:               Extrode LLC
Copyright:              Copyright (c) 2026 Extrode LLC. All rights reserved.
License:                Islamic Software License - Network Copyleft (ISL-NETC), Version 1.2
SPDX identifier:        LicenseRef-ISL-NETC-1.2
Governing jurisdiction: Commonwealth of Virginia
License text:           ./LICENSE.md   ·   https://islamiclicense.org/isl-netc/1.2/LICENSE.md
```

The URL names the version on purpose. `https://islamiclicense.org/isl-netc/` always serves the
newest text, so an unversioned link would drift away from the `LICENSE.md` next to this file.

## Who the Licensor is

**`LICENSE.md` §1.2 names Extrode LLC.** ISL-NETC 1.2 leaves §1.2 as a `[LICENSOR]` field for
whoever applies the license to fill in. Its *Notice on Reproducing This License* says that filling
in that field is not a modification, so `LICENSE.md` is still an unmodified copy of ISL-NETC 1.2.
It differs from the published text on that one line.

## Two copyrights, not one

| What | Held by |
|---|---|
| The **text of ISL-NETC 1.2** in `LICENSE.md` | Ikhbat Foundation (ikhbat.org), reproduced under its own reproduction notice with §1.2 filled in |
| The **software** the license is applied to | Extrode LLC |

Ikhbat Foundation did not write, publish or endorse this software. ISL-NETC §12 (No Endorsement
and Trademarks) applies: using the license gives no right to use the Foundation's or Extrode's
names beyond describing where the work came from and reproducing this file.

## What binds an operator

ISL-NETC is a network copyleft license. Two obligations reach further than a permissive license
would, and neither is waived here:

- **§3.6 Remote Network Interaction.** If you run a modified version of this software as a hosted
  service, you must offer its users the Corresponding Source of *that exact modified version*, at
  no charge, under ISL-NETC. Never distributing a copy does not get you out of it.
- **§§4 and 5 Ethical Use Restrictions.** These bind every recipient, every derivative work and
  every user of a network-operated modified version. §3.2 forbids relicensing under terms that
  remove, narrow or waive them.

## Third-party components

These are not covered by ISL-NETC. Each keeps its own license, and a copy of that license sits
next to the files.

| Component | Where | License |
|---|---|---|
| htmx | `apps/sample/wwwroot/vendor/htmx/`, `skeleton/…/vendor/htmx/` | Zero-Clause BSD |
| Alpine.js | `apps/sample/wwwroot/vendor/alpinejs/`, `skeleton/…/vendor/alpinejs/` | MIT |
| Instrument Sans, JetBrains Mono, Sora | `apps/sample/wwwroot/vendor/fonts/`, `skeleton/…/vendor/fonts/` | SIL Open Font License 1.1 |

**JauntyQ** is a separate product under ISL-R 1.2 and is not part of this repository. It is
restored from nuget.org as the `Extrode.JauntyQ.*` packages. Code its generator writes into a
build is covered by its Output Exception, and `Extrode.JauntyQ.Runtime` may ship inside built
output under its Redistribution Exception. Nothing else of JauntyQ's is redistributed here.

## Standing caveat

ISL-NETC 1.2 says of itself that it "is provided as a legal template and should be reviewed by a
qualified attorney before use in production," and that its drafters take no liability for its
legal sufficiency in any jurisdiction. No attorney has reviewed it for Extrode LLC. It is not an
OSI-approved open-source license and does not claim to be one.

Contribution terms are in [`CONTRIBUTING.md`](./CONTRIBUTING.md).
