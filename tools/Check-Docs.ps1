[CmdletBinding()]
param([switch]$Fix)

# Checks that the docs name the version in Directory.Build.props, and that AGENTS.md is a copy of CLAUDE.md.
# With -Fix, AGENTS.md is replaced by a copy of CLAUDE.md. Version strings are not rewritten: edit them by hand.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$propsText = [System.IO.File]::ReadAllText((Join-Path $root 'Directory.Build.props'))
$expected = [regex]::Match($propsText, '<RimWorldVersion>([^<]+)</RimWorldVersion>').Groups[1].Value
if (-not $expected) { throw 'RimWorldVersion is not set in Directory.Build.props.' }
# ADR 0012: the compile reference may be pinned to an older package while NuGet has none for the target. Say so on every check, so the
# pin is not forgotten once the matching package exists.
$package = [regex]::Match($propsText, '<RimWorldPackageVersion>([^<]+)</RimWorldPackageVersion>').Groups[1].Value
$derived = $expected.Split(' ')[0] + '-unstable'
if ($package -and $package -notmatch '\$\(' -and $package -ne $derived) {
    Write-Host "Note: the compile reference is pinned to Krafs.Rimworld.Ref $package, not $derived (ADR 0012). Remove the pin once NuGet publishes $derived."
}

$problems = @()
foreach ($name in @('README.md', 'CLAUDE.md')) {
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadLines((Join-Path $root $name))) {
        $lineNumber++
        foreach ($match in [regex]::Matches($line, '1\.7\.\d{4} rev\d+')) {
            if ($match.Value -ne $expected) {
                $problems += "$name line ${lineNumber}: names $($match.Value); expected $expected"
            }
        }
    }
}

$claudePath = Join-Path $root 'CLAUDE.md'
$agentsPath = Join-Path $root 'AGENTS.md'
if ($Fix) {
    Copy-Item -LiteralPath $claudePath -Destination $agentsPath -Force
}
$claude = [System.IO.File]::ReadAllText($claudePath).Replace("`r`n", "`n")
$agents = [System.IO.File]::ReadAllText($agentsPath).Replace("`r`n", "`n")
if ($agents -ne $claude) {
    $problems += 'AGENTS.md differs from CLAUDE.md. Run tools\Check-Docs.ps1 -Fix.'
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host $_ }
    exit 1
}
Write-Host "Docs name $expected, and AGENTS.md matches CLAUDE.md."
exit 0
