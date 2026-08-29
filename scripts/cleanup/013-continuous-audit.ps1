# Cleanup for spec 013 (continuous read-only audit).
#
# Bare invocation is the dry run. -Execute is the only way to act. Removing the
# repository mirror that landed on the audit host needs -DropRemoteMirror as well,
# because that one is on another machine and is not recoverable from this tree.
#
# Nothing here is run by an agent. The user runs it.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [switch]$DropRemoteMirror,
    [string]$AuditHost = 'audit-host.local'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$scratch = @(
    @{ path = 'tmp/audit-smoke';    why = 'smoke-run store from tuning the doc rule against real slices' },
    @{ path = 'tmp/audit-doc2';     why = 'store from the dogfood run over docs/02-reference' },
    @{ path = 'tmp/audit-bak';      why = 'restore points used while mutation-testing run.ts' },
    @{ path = 'tmp/ledger.bak';     why = 'restore point, ledger.ts mutation testing' },
    @{ path = 'tmp/redact.bak';     why = 'restore point, redact.ts mutation testing' },
    @{ path = 'tmp/slices.bak';     why = 'restore point, slices.ts mutation testing' },
    @{ path = 'tmp/ledger-first.bak'; why = 'an early fallback wrote this to the repo root; moved here, never used since' },
    @{ path = 'tmp/probe-audit.ts'; why = 'one-off probe that disproved an assertion about what the ledger stores' }
)

function Show-Target($rel, $why) {
    $full = Join-Path $repo $rel
    if (Test-Path $full) {
        Write-Host "  would remove: $rel  ($why)"
        return $full
    }
    Write-Host "  already gone: $rel"
    return $null
}

Write-Host "Scratch created by spec 013 (all under tmp/, gitignored):"
$scratchPaths = @()
foreach ($item in $scratch) {
    $hit = Show-Target $item.path $item.why
    if ($hit) { $scratchPaths += $hit }
}

Write-Host ""
Write-Host "Gated -- needs -DropRemoteMirror as well as -Execute:"
Write-Host "  ${AuditHost}:~/audit/mirror/nativeweb.git"
Write-Host "    A full mirror of this repository, pushed on 2026-08-29 at 14:49 by a mutation"
Write-Host "    test: the mutation removed the dry run's early exit and the script went on to"
Write-Host "    push for real. Nothing reached origin. The test now stubs ssh and asserts it was"
Write-Host "    never invoked."
Write-Host ""
Write-Host "    Keep it if you are going ahead with the audit host -- it is the mirror the"
Write-Host "    nightly run reads, and sync-to-audit-host.sh would recreate it. Remove it if you are"
Write-Host "    not, because a second host holding every branch of this repository is not"
Write-Host "    something to leave lying around by accident."

Write-Host ""
Write-Host "NOT removed by this script, and not candidates:"
Write-Host "  the feat/013-continuous-audit branch -- branches are never deleted here"
Write-Host "  ~/audit/{ledger,runs,reports} on the audit host -- the findings and their decisions"
Write-Host "  ~/.deno on the audit host -- installed by provision-audit-host.sh, harmless, and needed if"
Write-Host "    the audit is ever restarted"
Write-Host "  the launchd agent com.extrode.nativeweb.audit -- stopping the schedule is"
Write-Host "    'launchctl bootout', documented in docs/02-reference/continuous-audit.md, and is"
Write-Host "    a decision about the feature rather than cleanup after it"

if (-not $Execute) {
    Write-Host ""
    Write-Host "Dry run. Nothing was changed. Re-run with -Execute to remove the scratch above."
    exit 0
}

foreach ($p in $scratchPaths) {
    Remove-Item -LiteralPath $p -Recurse -Force
    Write-Host "removed: $p"
}

if ($DropRemoteMirror) {
    Write-Host ""
    Write-Host "Removing the mirror on $AuditHost ..."
    ssh -o BatchMode=yes $AuditHost 'rm -rf ~/audit/mirror/nativeweb.git'
    if ($LASTEXITCODE -ne 0) { throw "ssh to $AuditHost failed with $LASTEXITCODE" }
    Write-Host "removed: ${AuditHost}:~/audit/mirror/nativeweb.git"
    Write-Host "The nightly run has nothing to clone until sync-to-audit-host.sh --execute is run again."
} else {
    Write-Host ""
    Write-Host "Kept the mirror on $AuditHost. Add -DropRemoteMirror to remove it too."
}
