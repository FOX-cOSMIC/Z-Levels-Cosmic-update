# Builds and removes a RimWorld game copy for one validation run.
# The copy shares the install's large read-only folders through junctions and has its own Mods folder.
# Nothing in the install is written. Remove-ValidationGame deletes links before anything recursive, so it never reaches the install.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:MarkerName = 'zlevels-validation-game.json'
$script:HardLinkedFiles = @('Assets.zip')

function Invoke-CmdTool {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & cmd.exe /c @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "cmd.exe $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function Get-ValidationGameMarker {
    param([Parameter(Mandatory)][string]$Destination)
    $path = Join-Path $Destination $script:MarkerName
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function New-ValidationGame {
    param(
        [Parameter(Mandatory)][string]$SourceGame,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$RunId,
        [string[]]$ModFolders = @()
    )
    $source = (Resolve-Path -LiteralPath $SourceGame).Path
    if (-not (Test-Path -LiteralPath (Join-Path $source 'RimWorld.exe'))) { throw "RimWorld.exe is not in $source." }
    if (Test-Path -LiteralPath $Destination) {
        $marker = Get-ValidationGameMarker -Destination $Destination
        if ($null -eq $marker -or $marker.RunId -ne $RunId) {
            throw "Existing folder is not a validation game copy from this run: $Destination"
        }
        Remove-ValidationGame -Destination $Destination
    }
    New-Item -ItemType Directory -Path $Destination | Out-Null

    foreach ($directory in Get-ChildItem -LiteralPath $source -Directory -Force) {
        if ($directory.Name -eq 'Mods') { continue }
        Invoke-CmdTool -Arguments @('mklink', '/J', (Join-Path $Destination $directory.Name), $directory.FullName)
    }
    foreach ($file in Get-ChildItem -LiteralPath $source -File -Force) {
        $target = Join-Path $Destination $file.Name
        if ($script:HardLinkedFiles -contains $file.Name) {
            try { Invoke-CmdTool -Arguments @('mklink', '/H', $target, $file.FullName) }
            catch { Copy-Item -LiteralPath $file.FullName -Destination $target }
        }
        else {
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
    }

    $mods = Join-Path $Destination 'Mods'
    New-Item -ItemType Directory -Path $mods | Out-Null
    foreach ($folder in $ModFolders) {
        Copy-Item -LiteralPath $folder -Destination (Join-Path $mods (Split-Path -Leaf $folder)) -Recurse
    }

    $marker = [pscustomobject]@{ RunId = $RunId; SourceGame = $source; CreatedUtc = (Get-Date).ToUniversalTime().ToString('o') }
    [IO.File]::WriteAllText((Join-Path $Destination $script:MarkerName), ($marker | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
    [pscustomobject]@{ Root = $Destination; Executable = (Join-Path $Destination 'RimWorld.exe') }
}

function Remove-ValidationGame {
    param([Parameter(Mandatory)][string]$Destination)
    $marker = Get-ValidationGameMarker -Destination $Destination
    if ($null -eq $marker) { throw "Not a validation game copy (no run marker): $Destination" }
    $source = $marker.SourceGame
    foreach ($item in Get-ChildItem -LiteralPath $Destination -Force) {
        if (-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
        $expected = [IO.Path]::GetFullPath((Join-Path $source $item.Name)).TrimEnd('\')
        $actual = @($item.Target | ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\') })
        if ($actual.Count -ne 1 -or -not ($actual[0] -ieq $expected)) {
            throw "Refusing to remove a link that does not point at the recorded source: $($item.FullName)"
        }
        Invoke-CmdTool -Arguments @('rmdir', $item.FullName)
    }
    Remove-Item -LiteralPath $Destination -Recurse -Force
}

Export-ModuleMember -Function New-ValidationGame, Remove-ValidationGame
