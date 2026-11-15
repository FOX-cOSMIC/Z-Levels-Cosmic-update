# Builds the mod from a clean tree and copies its DLLs into the tracked release folders: ZLevels/1.7/Assemblies for the mod, and
# ZLevels/1.7-Rimatomics/Assemblies for the cooling add-on, which LoadFolders.xml loads only when Dubs Rimatomics is active. This script is
# the only writer of those folders. It refuses uncommitted changes to the mod sources, so the DLLs always match a commit. Machine paths are
# mapped away in Directory.Build.props (PathMap), so the bytes carry none.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$dirty = @(git -C $root status --porcelain --untracked-files=normal -- ZLevels Directory.Build.props RimWorld.targets ':!ZLevels/1.7/Assemblies' ':!ZLevels/1.7-Rimatomics/Assemblies')
if ($LASTEXITCODE -ne 0) { throw 'git status failed.' }
if ($dirty.Count -gt 0) {
    $dirty | ForEach-Object { Write-Host $_ }
    throw 'Uncommitted changes to the mod sources are listed above. Commit or revert them before publishing.'
}

$commit = (git -C $root rev-parse HEAD).Trim()
$propsText = [IO.File]::ReadAllText((Join-Path $root 'Directory.Build.props'))
$rimWorldVersion = [regex]::Match($propsText, '<RimWorldVersion>([^<]+)</RimWorldVersion>').Groups[1].Value
$sdk = (dotnet --version).Trim()

$modProject = Join-Path $root 'ZLevels/Source/ZLevels/ZLevels/ZLevels.csproj'
$rimatomicsProject = Join-Path $root 'ZLevels/Source/ZLevels/ZLevelsRimatomics/ZLevelsRimatomics.csproj'
foreach ($project in @($modProject, $rimatomicsProject)) {
    & dotnet build $project -c Release -t:Rebuild --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}

$modBin = Join-Path $root 'ZLevels/Source/ZLevels/ZLevels/bin/Release'
$rimatomicsBin = Join-Path $root 'ZLevels/Source/ZLevels/ZLevelsRimatomics/bin/Release'
$outputs = [ordered]@{
    'ZLevels.dll' = Join-Path $modBin 'ZLevels.dll'
    'ZLevelsRimatomics.dll' = Join-Path $rimatomicsBin 'ZLevelsRimatomics.dll'
    '0MultiplayerAPI.dll' = Join-Path $modBin '0MultiplayerAPI.dll'
}
$release = Join-Path $root 'ZLevels/1.7/Assemblies'
# The add-on has its own folder. A copy in the Z-Levels folder would load it without Dubs Rimatomics.
$releaseFolders = @{ 'ZLevelsRimatomics.dll' = (Join-Path $root 'ZLevels/1.7-Rimatomics/Assemblies') }
$profile = $env:USERPROFILE
$lines = @("Built from source commit: $commit", "RimWorld: $rimWorldVersion", ".NET SDK: $sdk")
foreach ($name in $outputs.Keys) {
    $source = $outputs[$name]
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing build output: $source" }
    $folder = if ($releaseFolders.ContainsKey($name)) { $releaseFolders[$name] } else { $release }
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $target = Join-Path $folder $name
    Copy-Item -LiteralPath $source -Destination $target -Force
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($sourceHash -ne $targetHash) { throw "The copy does not match its build output: $name" }
    $text = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($target))
    if ($text.Contains($profile) -or [regex]::IsMatch($text, '[A-Za-z]:\\')) {
        throw "$name contains a machine path. Check the PathMap in Directory.Build.props."
    }
    $lines += "$name SHA-256: $sourceHash"
}
# A copy of the add-on left in the Z-Levels folder by an older publish would load without Rimatomics, so it is removed here.
$staleAddon = Join-Path $release 'ZLevelsRimatomics.dll'
if (Test-Path -LiteralPath $staleAddon) { Remove-Item -LiteralPath $staleAddon }
[IO.File]::WriteAllLines((Join-Path $release 'build-info.txt'), $lines, (New-Object Text.UTF8Encoding $false))
$lines
