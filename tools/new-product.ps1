# new-product.ps1 -- stamp a new product repo from skeleton/ (spec 003 R2).
#
# Copies the tokenized skeleton to a destination directory, renames every
# __product__ path segment, substitutes __PRODUCT__ / __product__ inside text
# files, and (with -Execute) initialises git with an initial commit on `main`
# and a `dev` branch, matching the mandatory branch model.
#
# Dry run is the default: a bare invocation prints every action and changes
# nothing. -Execute / -e is the only way to act. A dry run always exits 0;
# an -Execute run exits non-zero if a precondition fails.
#
# Usage:
#   pwsh tools/new-product.ps1 -Name Manzil -Slug manzil -Destination C:\home\code\extrode-legacy-org.com\manzil.my
#   pwsh tools/new-product.ps1 -Name Manzil -Slug manzil -Destination ... -Execute

[CmdletBinding()]
param(
    # PascalCase product name: namespaces, config section, page titles.
    [Parameter(Mandatory = $true)][string]$Name,
    # Lowercase slug: file names, csproj names, database name, service role
    # (<slug>_app), systemd unit. Defaults to the name lowercased.
    [string]$Slug,
    # Directory to create the product in. Must not already exist (or be empty).
    [Parameter(Mandatory = $true)][string]$Destination,
    [Alias('e')][switch]$Execute
)

$ErrorActionPreference = 'Stop'

if (-not $Slug) { $Slug = $Name.ToLowerInvariant() }

# The name feeds C# namespaces and the slug feeds Postgres identifiers and
# project file names, so both are validated up front rather than discovered
# as a broken build later. No hyphens in the slug: it becomes the database
# name and the <slug>_app role.
if ($Name -cnotmatch '^[A-Z][A-Za-z0-9]*$') {
    Write-Error "Name must be PascalCase alphanumeric (got '$Name')."
}
if ($Slug -cnotmatch '^[a-z][a-z0-9]*$') {
    Write-Error "Slug must be lowercase alphanumeric, no hyphens/underscores (got '$Slug')."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$skeleton = Join-Path $repoRoot 'skeleton'
if (-not (Test-Path $skeleton)) {
    Write-Error "skeleton/ not found at $skeleton -- run from a NativeWeb checkout."
}

if ((Test-Path $Destination) -and (Get-ChildItem -Force $Destination | Select-Object -First 1)) {
    if ($Execute) { Write-Error "Destination $Destination exists and is not empty." }
    Write-Host "NOTE: destination $Destination exists and is not empty -- -Execute would refuse."
}

$mode = if ($Execute) { 'EXECUTE' } else { 'DRY RUN (pass -Execute / -e to act)' }
Write-Host "== new-product: $Name ($Slug) -> $Destination [$mode] =="

# Only files with these extensions (or these exact names) get token
# substitution; everything else (fonts, images, vendored minified JS) is
# copied byte-for-byte.
$textExtensions = @('.cs', '.csproj', '.props', '.slnx', '.json', '.html', '.md',
    '.yml', '.yaml', '.sh', '.ps1', '.txt', '.xml', '.css', '.sql', '.service',
    '.editorconfig', '.gitignore')
$textNames = @('.editorconfig', '.gitignore', '.gitkeep', 'CLAUDE.md')

function Test-IsTextFile([System.IO.FileInfo]$file) {
    return ($textExtensions -contains $file.Extension.ToLowerInvariant()) -or
           ($textNames -contains $file.Name)
}

$files = Get-ChildItem -Recurse -File -Force $skeleton
Write-Host "-- 1. copy $($files.Count) files from skeleton/"

$actions = @()
foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($skeleton, $file.FullName)
    $target = $relative.Replace('__product__', $Slug).Replace('__PRODUCT__', $Name)
    $isText = Test-IsTextFile $file
    $stamp = $false
    if ($isText) {
        $content = Get-Content -Raw -LiteralPath $file.FullName
        $stamp = $content -and ($content.Contains('__PRODUCT__') -or $content.Contains('__product__'))
    }
    $actions += [pscustomobject]@{ Source = $file.FullName; Relative = $relative; Target = $target; Stamp = $stamp }
    $verb = if ($stamp) { 'stamp' } elseif ($relative -ne $target) { 'rename' } else { 'copy ' }
    Write-Host "   $verb $relative$(if ($relative -ne $target) { " -> $target" })"
}

Write-Host '-- 2. git init (main, initial commit, dev branch)'
Write-Host "-- 3. reminder: set NativeWebRoot/JauntyQRoot or -p:NativeWebUsePackages=true; see the stamped README"

if (-not $Execute) {
    Write-Host 'Dry run complete; nothing written.'
    exit 0
}

New-Item -ItemType Directory -Force $Destination | Out-Null
foreach ($action in $actions) {
    $dest = Join-Path $Destination $action.Target
    New-Item -ItemType Directory -Force (Split-Path -Parent $dest) | Out-Null
    if ($action.Stamp) {
        $content = Get-Content -Raw -LiteralPath $action.Source
        $content = $content.Replace('__PRODUCT__', $Name).Replace('__product__', $Slug)
        # Preserve the file's own line endings and no-final-newline quirks by
        # writing raw text, UTF-8 without BOM (the repo convention).
        [System.IO.File]::WriteAllText($dest, $content, [System.Text.UTF8Encoding]::new($false))
    }
    else {
        Copy-Item -LiteralPath $action.Source -Destination $dest
    }
}

# git does not honor $ErrorActionPreference (native exe), so every call is
# checked -- a failure here (no user.email, hook rejection) must not print
# success over a half-initialised repo.
function Invoke-Git([string[]]$gitArguments) {
    git @gitArguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "git $($gitArguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Push-Location $Destination
try {
    Invoke-Git @('init', '--initial-branch=main')
    Invoke-Git @('add', '-A')
    Invoke-Git @('commit', '-m', "chore: stamp $Slug from the NativeWeb skeleton")
    Invoke-Git @('branch', 'dev')
}
finally {
    Pop-Location
}

Write-Host "Stamped $Name into $Destination (git: main + dev)."
Write-Host 'Next: create the database + role (see db/README.md), run the migrate script, then build in checkout mode.'
exit 0
