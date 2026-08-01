# extract-manzil.ps1 -- extract DevMarks from NativeWeb into the standalone
# manzil.my product repo (spec 003 V10, R4-R7, R42).
#
# Copies apps/devmarks, libs/nativeweb.devmarks.data, the three
# tests/nativeweb.devmarks.tests.* projects and db/devmarks through an ordered
# rename map (nativeweb.devmarks.data -> manzil.data, nativeweb.devmarks ->
# manzil, NativeWeb.DevMarks -> Manzil, DevMarks -> Manzil, devmarks -> manzil
# -- which also carries devmarks_dev -> manzil_dev and devmarks_app ->
# manzil_app), stamps the non-app skeleton/ scaffolding (root files, .github,
# deploy, docs, scripts, work) with Manzil/manzil, repoints the app csproj's
# framework references to the $(NativeWebRoot) dual-mode split, and (with
# -Execute) initialises git with an initial commit on `main` and a `dev`
# branch. Fresh history: nothing from NativeWeb's log is carried over.
#
# Dry run is the default: a bare invocation prints every action and changes
# nothing. -Execute / -e is the only way to act. A dry run always exits 0;
# an -Execute run exits non-zero if a precondition or patch anchor fails.
#
# Usage:
#   pwsh scripts/extract-manzil.ps1
#   pwsh scripts/extract-manzil.ps1 -Execute

[CmdletBinding()]
param(
    [string]$Destination = 'C:\home\code\extrode-legacy-org.com\manzil.my',
    [Alias('e')][switch]$Execute
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$skeleton = Join-Path $repoRoot 'skeleton'
foreach ($required in @($skeleton,
        (Join-Path $repoRoot 'apps\devmarks'),
        (Join-Path $repoRoot 'libs\nativeweb.devmarks.data'),
        (Join-Path $repoRoot 'db\devmarks'))) {
    if (-not (Test-Path $required)) {
        Write-Error "Expected $required -- run from a NativeWeb checkout that still has DevMarks."
    }
}

if ((Test-Path $Destination) -and (Get-ChildItem -Force $Destination | Select-Object -First 1)) {
    if ($Execute) { Write-Error "Destination $Destination exists and is not empty." }
    Write-Host "NOTE: destination $Destination exists and is not empty -- -Execute would refuse."
}

$mode = if ($Execute) { 'EXECUTE' } else { 'DRY RUN (pass -Execute / -e to act)' }
Write-Host "== extract-manzil: $repoRoot -> $Destination [$mode] =="

# Ordered: longest / most specific first, so nativeweb.devmarks.data becomes
# manzil.data before the bare nativeweb.devmarks token would eat its prefix,
# and the possessive "DevMarks'" becomes "Manzil's" before the plain token
# would leave the ungrammatical "Manzil'". NativeWeb.slnx is the e2e fixture's
# repo-root sentinel; in the new repo the solution is manzil.slnx.
# String.Replace is ordinal and case-sensitive, which the map relies on.
# An array of pairs, not a hashtable: PowerShell hashtable keys are
# case-insensitive, so 'DevMarks' and 'devmarks' would collide.
$renameMap = @(
    @('NativeWeb.slnx',          'manzil.slnx'),
    @('nativeweb-devmarks',      'manzil'),
    @("DevMarks'",               "Manzil's"),
    @('nativeweb.devmarks.data', 'manzil.data'),
    @('nativeweb.devmarks',      'manzil'),
    @('NativeWeb.DevMarks',      'Manzil'),
    @('DevMarks',                'Manzil'),
    @('devmarks',                'manzil')
)

function Convert-Name([string]$value) {
    foreach ($entry in $renameMap) {
        $value = $value.Replace($entry[0], $entry[1])
    }
    return $value
}

# Same text-detection rule as tools/new-product.ps1: only these get token
# substitution; fonts, images and vendored minified JS copy byte-for-byte.
$textExtensions = @('.cs', '.csproj', '.props', '.slnx', '.json', '.html', '.md',
    '.yml', '.yaml', '.sh', '.ps1', '.txt', '.xml', '.css', '.sql', '.service',
    '.editorconfig', '.gitignore')
$textNames = @('.editorconfig', '.gitignore', '.gitkeep', 'CLAUDE.md')

function Test-IsTextFile([System.IO.FileInfo]$file) {
    return ($textExtensions -contains $file.Extension.ToLowerInvariant()) -or
           ($textNames -contains $file.Name)
}

function Write-Utf8NoBom([string]$path, [string]$content) {
    [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
}

# ---------------------------------------------------------------------------
# 1. The moved projects, through the rename map. db/devmarks flattens to db/:
#    the product owns its whole database, so the per-app subdirectory goes.
# ---------------------------------------------------------------------------
$copySets = @(
    @{ Source = 'apps\devmarks';                          Dest = 'apps\manzil' }
    @{ Source = 'libs\nativeweb.devmarks.data';           Dest = 'libs\manzil.data' }
    @{ Source = 'tests\nativeweb.devmarks.tests.unit';        Dest = 'tests\manzil.tests.unit' }
    @{ Source = 'tests\nativeweb.devmarks.tests.integration'; Dest = 'tests\manzil.tests.integration' }
    @{ Source = 'tests\nativeweb.devmarks.tests.e2e';         Dest = 'tests\manzil.tests.e2e' }
    @{ Source = 'db\devmarks';                            Dest = 'db' }
)

$actions = @()
foreach ($set in $copySets) {
    $sourceRoot = Join-Path $repoRoot $set.Source
    $files = Get-ChildItem -Recurse -File -Force $sourceRoot |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    Write-Host "-- copy $($set.Source) -> $($set.Dest) ($($files.Count) files)"
    foreach ($file in $files) {
        $relative = [System.IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
        $target = Join-Path $set.Dest (Convert-Name $relative)
        $isText = Test-IsTextFile $file
        $actions += [pscustomobject]@{ Source = $file.FullName; Target = $target; Stamp = $isText; Kind = 'move' }
        $verb = if ($isText) { 'stamp' } else { 'copy ' }
        Write-Host "   $verb $($set.Source)\$relative -> $target"
    }
}

# ---------------------------------------------------------------------------
# 2. Skeleton scaffolding for everything the moved projects do not bring:
#    root files, .github, deploy, docs, scripts, work. The skeleton's own
#    apps/libs/tests/db are the parts DevMarks replaces, so they are skipped.
# ---------------------------------------------------------------------------
$skeletonSkip = @('apps', 'libs', 'tests', 'db')
$skeletonFiles = Get-ChildItem -Recurse -File -Force $skeleton | Where-Object {
    $rel = [System.IO.Path]::GetRelativePath($skeleton, $_.FullName)
    $top = $rel.Split([System.IO.Path]::DirectorySeparatorChar)[0]
    $skeletonSkip -notcontains $top
}
Write-Host "-- stamp $($skeletonFiles.Count) skeleton scaffolding files (Manzil / manzil)"
foreach ($file in $skeletonFiles) {
    $relative = [System.IO.Path]::GetRelativePath($skeleton, $file.FullName)
    $target = $relative.Replace('__product__', 'manzil').Replace('__PRODUCT__', 'Manzil')
    $actions += [pscustomobject]@{ Source = $file.FullName; Target = $target; Stamp = (Test-IsTextFile $file); Kind = 'skeleton' }
    Write-Host "   stamp skeleton\$relative -> $target"
}

$collisions = $actions | Group-Object Target | Where-Object Count -gt 1
if ($collisions) {
    Write-Error "Copy plan writes these targets twice: $(($collisions.Name) -join ', ')"
}

# ---------------------------------------------------------------------------
# 3. Post-copy patches (exact-anchor, asserted): the app csproj's framework
#    references move to the skeleton's $(NativeWebRoot) dual-mode split, and
#    the data csproj's AdditionalFiles follow db/devmarks -> db/.
# ---------------------------------------------------------------------------
$patches = @(
    @{
        File = 'apps\manzil\manzil.csproj'
        Old  = @'
    <!-- Manzil's own JauntyQ generated types (db/manzil). -->
    <ProjectReference Include="..\..\libs\manzil.data\manzil.data.csproj" />
    <!-- Passphrases (Argon2id), OpaqueTokens, Sha256Hex. -->
    <ProjectReference Include="..\..\libs\nativeweb.security\nativeweb.security.csproj" />
    <!-- LockoutPolicy, breach/common-passphrase checkers. -->
    <ProjectReference Include="..\..\libs\nativeweb.auth\nativeweb.auth.csproj" />
    <!-- Compiled-template render engine: runtime + the source generator that
         turns templates/**/*.html and locales/*.json below into C#. -->
    <ProjectReference Include="..\..\libs\nativeweb.render\nativeweb.render.csproj" />
    <ProjectReference Include="..\..\libs\nativeweb.render.generator\nativeweb.render.generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
'@
        New  = @'
    <!-- Manzil's own JauntyQ generated types (db/). -->
    <ProjectReference Include="..\..\libs\manzil.data\manzil.data.csproj" />
  </ItemGroup>

  <!-- Framework libs: local checkout via ProjectReference for co-development;
       pinned NuGet packages in package mode (-p:NativeWebUsePackages=true, e.g.
       CI with no sibling checkout). Same dual-mode split JauntyQ uses in
       libs/manzil.data. -->
  <ItemGroup Condition="'$(NativeWebUsePackages)' != 'true'">
    <!-- Passphrases (Argon2id), OpaqueTokens, Sha256Hex, BotTraps. -->
    <ProjectReference Include="$(NativeWebRoot)\libs\nativeweb.security\nativeweb.security.csproj" />
    <!-- LockoutPolicy, breach/common-passphrase checkers. -->
    <ProjectReference Include="$(NativeWebRoot)\libs\nativeweb.auth\nativeweb.auth.csproj" />
    <!-- Compiled-template render engine: runtime + the source generator that
         turns templates/**/*.html and locales/*.json below into C#. -->
    <ProjectReference Include="$(NativeWebRoot)\libs\nativeweb.render\nativeweb.render.csproj" />
    <ProjectReference Include="$(NativeWebRoot)\libs\nativeweb.render.generator\nativeweb.render.generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

  <ItemGroup Condition="'$(NativeWebUsePackages)' == 'true'">
    <PackageReference Include="NativeWeb.Security" />
    <PackageReference Include="NativeWeb.Auth" />
    <PackageReference Include="NativeWeb.Render" />
    <!-- The package ships under analyzers/dotnet/cs, which is what makes it a
         generator; OutputItemType/ReferenceOutputAssembly are ProjectReference
         metadata and would be ignored here. PrivateAssets keeps it build-only. -->
    <PackageReference Include="NativeWeb.Render.Generator" PrivateAssets="all" />
  </ItemGroup>
'@
    }
    @{
        File = 'libs\manzil.data\manzil.data.csproj'
        Old  = '    <AdditionalFiles Include="..\..\db\manzil\queries\**\*.sql" />'
        New  = '    <AdditionalFiles Include="..\..\db\queries\**\*.sql" />'
    }
    @{
        File = 'libs\manzil.data\manzil.data.csproj'
        Old  = '    <AdditionalFiles Include="..\..\db\manzil\schema\*.schema.json" />'
        New  = '    <AdditionalFiles Include="..\..\db\schema\*.schema.json" />'
    }
    @{
        File = 'libs\manzil.data\manzil.data.csproj'
        Old  = 'globbing db/manzil/'
        New  = 'globbing db/'
    }
)
Write-Host "-- $($patches.Count) exact-anchor patches (framework refs -> `$(NativeWebRoot); db/manzil -> db/)"
foreach ($patch in $patches) {
    Write-Host "   patch $($patch.File)"
}
Write-Host '-- git init (main, initial commit, dev branch)'
Write-Host '-- residual scan: any devmarks / __product__ token left is reported'

if (-not $Execute) {
    Write-Host 'Dry run complete; nothing written.'
    exit 0
}

# ---------------------------------------------------------------------------
# Execute.
# ---------------------------------------------------------------------------
New-Item -ItemType Directory -Force $Destination | Out-Null
foreach ($action in $actions) {
    $dest = Join-Path $Destination $action.Target
    New-Item -ItemType Directory -Force (Split-Path -Parent $dest) | Out-Null
    if ($action.Stamp) {
        $content = Get-Content -Raw -LiteralPath $action.Source
        if ($null -eq $content) { $content = '' }
        if ($action.Kind -eq 'skeleton') {
            $content = $content.Replace('__PRODUCT__', 'Manzil').Replace('__product__', 'manzil')
        }
        else {
            $content = Convert-Name $content
        }
        Write-Utf8NoBom $dest $content
    }
    else {
        Copy-Item -LiteralPath $action.Source -Destination $dest
    }
}

foreach ($patch in $patches) {
    $path = Join-Path $Destination $patch.File
    $content = [System.IO.File]::ReadAllText($path)
    if (-not $content.Contains($patch.Old)) {
        Write-Error "Patch anchor not found in $($patch.File): $($patch.Old.Substring(0, [Math]::Min(70, $patch.Old.Length)))..."
    }
    Write-Utf8NoBom $path $content.Replace($patch.Old, $patch.New)
}

# Residual scan: a leftover token means the rename map missed something.
$residuals = Get-ChildItem -Recurse -File -Force $Destination |
    Where-Object { Test-IsTextFile $_ } |
    Select-String -Pattern 'devmarks|__product__|__PRODUCT__' -List
if ($residuals) {
    $residuals | ForEach-Object { Write-Host "RESIDUAL: $($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
    Write-Error "Rename left $(@($residuals).Count) file(s) with devmarks/__product__ tokens (listed above)."
}

# git does not honor $ErrorActionPreference (native exe), so every call is
# checked -- a failure here must not print success over a half-made repo.
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
    Invoke-Git @('commit', '-m', 'chore: extract manzil.my from NativeWeb (spec 003 V10)')
    Invoke-Git @('branch', 'dev')
}
finally {
    Pop-Location
}

Write-Host "Extracted manzil.my into $Destination (git: main + dev, fresh history)."
Write-Host 'Next: build in checkout mode (sibling NativeWeb + jauntyq checkouts resolve via'
Write-Host 'Directory.Build.props probes) and run the three suites; see the stamped README.'
exit 0
