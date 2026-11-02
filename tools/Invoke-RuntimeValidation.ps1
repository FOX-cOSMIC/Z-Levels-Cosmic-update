param(
    [string]$RimWorldDirectory = 'C:/SteamLibrary/steamapps/common/RimWorld',
    [string]$HarmonyAssembly,
    [switch]$Visual,
    [switch]$InfestationGuard,
    [switch]$StaleLookup,
    [switch]$VaultPlacement,
    [switch]$VaultPocket,
    [switch]$VaultEnter,
    [switch]$VaultLoad,
    [switch]$VaultSave,
    [switch]$VaultInfestation,
    [switch]$Supply,
    [switch]$AllColonistsHaul,
    [switch]$OtherColonistHauls,
    [switch]$FreeColonists,
    [switch]$OtherColonistsWork,
    [switch]$UnreachableZone,
    [switch]$MultiBuild,
    [switch]$MultiBuildLowerLogs,
    [switch]$StairsDamage,
    [switch]$Cooling,
    [switch]$CoolingNoColumns,
    [switch]$RimatomicsPlusPlus,
    [switch]$ZLevelsDebug,
    # A ModsConfig.xml whose mods run together with Z-Levels. It is only read. The isolated run gets a copy of its mod list.
    [string]$ModsConfigSource,
    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
# The first ERROR entry in a game log that names Z-Levels. An entry is the ERROR line and its continuation lines, up to the next timestamped
# line, read the way the $ownErrors block reads them. Returns the entry text, or $null when no entry names Z-Levels.
function Get-ZLevelsErrorEntry {
    param([string]$LogPath)
    foreach ($hit in Select-String -LiteralPath $LogPath -Pattern ' ERROR ' -Context 0,80) {
        $post = @($hit.Context.PostContext | ForEach-Object -Begin { $stop = $false } -Process { if ($stop) { return }; if ($_ -match '^\d{4}-\d{2}-\d{2} ') { $stop = $true; return }; $_ })
        $entry = (@($hit.Line) + $post) -join "`n"
        if ($entry -match 'Z-?Levels') { return $entry }
    }
    return $null
}
$workspace = Split-Path $PSScriptRoot -Parent
$game = (Resolve-Path -LiteralPath $RimWorldDirectory).Path
$executable = Join-Path $game 'RimWorld.exe'
if (Get-Process -Name RimWorld -ErrorAction SilentlyContinue) {
    throw 'Close the running RimWorld process before starting isolated validation.'
}
$runDirectory = Join-Path $workspace ('.artifacts/runs/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$configDirectory = Join-Path $runDirectory 'Config'
New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null

$buildArguments = @('build', (Join-Path $PSScriptRoot 'RuntimeValidation/RuntimeValidation.csproj'), '-c', 'Release', '--nologo', "-p:RimWorldDirectory=$game")
if ($HarmonyAssembly) { $buildArguments += "-p:HarmonyAssembly=$HarmonyAssembly" }
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'Validation build failed.' }

$installations = @{
    'ZLevels-Local17' = (Join-Path $workspace 'ZLevels')
    'ZLevels-Validation' = (Join-Path $PSScriptRoot 'RuntimeValidation')
}
foreach ($name in $installations.Keys) {
    $link = Join-Path (Join-Path $game 'Mods') $name
    $target = (Resolve-Path -LiteralPath $installations[$name]).Path
    if (Test-Path -LiteralPath $link) {
        $existing = Get-Item -LiteralPath $link
        if ($existing.LinkType -ne 'Junction' -or $existing.Target -notcontains $target) {
            throw "Existing mod folder is not this checkout's junction: $link"
        }
    }
    else { New-Item -ItemType Junction -Path $link -Target $target | Out-Null }
}

$configuration = @'
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <version>1.7.4990 rev1069</version>
  <activeMods>
__ACTIVE_MODS__
  </activeMods>
  <knownExpansions>
    <li>ludeon.rimworld.royalty</li>
    <li>ludeon.rimworld.ideology</li>
    <li>ludeon.rimworld.biotech</li>
    <li>ludeon.rimworld.anomaly</li>
    <li>ludeon.rimworld.odyssey</li>
  </knownExpansions>
</ModsConfigData>
'@
# Without -ModsConfigSource the run loads only the validation mods. With it, the run loads the source's mods in their order, then this mod.
# The source file is read, never written.
$activeMods = @('brrainz.harmony', 'ludeon.rimworld', 'zlevels1.1', 'local.zlevels.validation')
if ($ModsConfigSource) {
    [xml]$source = Get-Content -LiteralPath $ModsConfigSource -Raw -Encoding UTF8
    $activeMods = @($source.SelectNodes('/ModsConfigData/activeMods/li') | ForEach-Object { $_.InnerText } | Where-Object { $_ -and $_ -notin @('zlevels1.1', 'local.zlevels.validation') })
    $activeMods += @('zlevels1.1', 'local.zlevels.validation')
}
# The cooling runs need Dubs Rimatomics. Rimatomics++ depends on it, so -RimatomicsPlusPlus adds it too. Each mod goes before Z-Levels, unless the list already has it.
$rimatomicsMods = @()
if ($Cooling -or $CoolingNoColumns -or $RimatomicsPlusPlus) { $rimatomicsMods += 'dubwise.rimatomics' }
if ($RimatomicsPlusPlus) { $rimatomicsMods += 'nachotoast.rimatomicsplusplus' }
$rimatomicsMods = @($rimatomicsMods | Where-Object { $activeMods -notcontains $_ })
$activeMods = @($activeMods | ForEach-Object { if ($_ -eq 'zlevels1.1') { $rimatomicsMods }; $_ })
$configuration = $configuration.Replace('__ACTIVE_MODS__', (($activeMods | ForEach-Object { "    <li>$_</li>" }) -join "`n"))
$configuration | Set-Content -LiteralPath (Join-Path $configDirectory 'ModsConfig.xml') -Encoding UTF8
if ($ModsConfigSource) {
    # A fresh save has no language set, and Readable Numbers throws on that during load. Use the language from the source's Prefs.xml.
    $sourcePrefs = Join-Path (Split-Path -Parent $ModsConfigSource) 'Prefs.xml'
    $language = if (Test-Path -LiteralPath $sourcePrefs) { [regex]::Match((Get-Content -LiteralPath $sourcePrefs -Raw -Encoding UTF8), '<langFolderName>([^<]+)</langFolderName>').Groups[1].Value } else { '' }
    if ($language) {
        $prefsXml = @"
<?xml version="1.0" encoding="utf-8"?>
<PrefsData>
  <langFolderName>$language</langFolderName>
</PrefsData>
"@
        $prefsXml | Set-Content -LiteralPath (Join-Path $configDirectory 'Prefs.xml') -Encoding UTF8
    }
}
if ($Visual) {
    # Unity pauses frames while its window is unfocused; this keeps the hidden window rendering for screenshots.
    $prefs = @'
<?xml version="1.0" encoding="utf-8"?>
<PrefsData>
  <fullscreen>False</fullscreen>
  <screenWidth>1280</screenWidth>
  <screenHeight>720</screenHeight>
  <runInBackground>True</runInBackground>
</PrefsData>
'@
    $prefs | Set-Content -LiteralPath (Join-Path $configDirectory 'Prefs.xml') -Encoding UTF8
}
$arguments = @('-NoCrashDialog', '-quicktest', '-zlevelsvalidation', ('"-savedatafolder={0}"' -f $runDirectory))
if (!$Visual) { $arguments += '-Headless' } else { $arguments += '-zlevelsscreenshots' }
if ($InfestationGuard) { $arguments += '-zlevelsinfestation' }
if ($StaleLookup) { $arguments += '-zlevelsstalelookup' }
if ($VaultPlacement) { $arguments += '-zlevelsvaultplacement' }
if ($VaultPocket) { $arguments += '-zlevelsvaultpocket' }
if ($VaultEnter) { $arguments += '-zlevelsvaultenter' }
if ($VaultLoad) { $arguments += '-zlevelsvaultload' }
if ($VaultSave) { $arguments += '-zlevelsvaultsave' }
if ($VaultInfestation) { $arguments += '-zlevelsvaultinfestation' }
if ($Supply) { $arguments += '-zlevelssupply' }
if ($AllColonistsHaul) { $arguments += '-zlevelsallcolonistshaul' }
if ($OtherColonistHauls) { $arguments += '-zlevelsothercolonistshaul' }
if ($FreeColonists) { $arguments += '-zlevelsfreecolonists' }
if ($OtherColonistsWork) { $arguments += '-zlevelsothercolonistswork' }
if ($UnreachableZone) { $arguments += '-zlevelsunreachablezone' }
if ($MultiBuild) { $arguments += '-zlevelsmultibuild' }
if ($MultiBuildLowerLogs) { $arguments += '-zlevelsmultibuildlowerlogs' }
if ($StairsDamage) { $arguments += '-zlevelsstairsdamage' }
if ($Cooling) { $arguments += '-zlevelscooling' }
if ($CoolingNoColumns) { $arguments += '-zlevelscoolingnocolumns' }
if ($ZLevelsDebug) { $arguments += '-zlevelsdebug' }
$process = Start-Process -FilePath $executable -WorkingDirectory $game -ArgumentList $arguments -WindowStyle Hidden -PassThru
$gameLog = Join-Path $env:USERPROFILE 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log'
$limit = (Get-Date).AddSeconds($TimeoutSeconds)
while (!$process.HasExited -and (Get-Date) -lt $limit) {
    Start-Sleep -Seconds 5
    # When a mod throws during play-data load, the game resets the mod list and loops on errors. Stop it now instead of waiting out the timeout.
    if ($ModsConfigSource -and (Test-Path -LiteralPath $gameLog) -and (Select-String -LiteralPath $gameLog -Pattern 'Resetting mods config' -Quiet)) {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        Copy-Item -LiteralPath $gameLog -Destination (Join-Path $runDirectory 'Player.log')
        throw "The game reset the mod list while loading the mods, so the run stopped. See the Player.log in $runDirectory"
    }
}
if (!$process.HasExited) {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    throw "Validation timed out; isolated files are in $runDirectory"
}

$log = Join-Path $env:USERPROFILE 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log'
if (Test-Path -LiteralPath $log) {
    Get-Content -LiteralPath $log | Set-Content -LiteralPath (Join-Path $runDirectory 'Player.log') -Encoding UTF8
}
$resultsPath = Join-Path $runDirectory 'validation-results.txt'
if (!(Test-Path -LiteralPath $resultsPath)) { throw "Game exited without validation results: $runDirectory" }
$results = Get-Content -LiteralPath $resultsPath
$requiredPass = if ($StairsDamage) { 'stairs damage' } elseif ($MultiBuild) { 'multiple builds below' } elseif ($UnreachableZone) { 'unreachable zone' } elseif ($OtherColonistHauls) { 'other colonist hauls across levels' } elseif ($AllColonistsHaul) { 'all colonists haul' } elseif ($InfestationGuard) { 'infestation guard' } elseif ($StaleLookup) { 'stale look-up map' } elseif ($VaultPlacement) { 'vault placement' } elseif ($VaultPocket) { 'vault pocket' } elseif ($VaultEnter) { 'vault build inside' } elseif ($VaultLoad) { 'vault load' } elseif ($VaultSave) { 'vault save' } elseif ($VaultInfestation) { 'vault infestation' } elseif ($Supply) { 'cross-level supply' } elseif ($Cooling -or $CoolingNoColumns) { 'cooling link' } else { 'core validation' }
$results
if ($results -match '[|] FAIL:' -or !($results -match ('[|] ' + $requiredPass + ' [|] PASS$'))) {
    # The cause hint: the first ERROR entry in the run's copy of the game log that names Z-Levels, so a failing run names its cause.
    $playerLog = Join-Path $runDirectory 'Player.log'
    if (!(Test-Path -LiteralPath $playerLog)) {
        Write-Host 'No Player.log in the run folder.'
    }
    else {
        $zlevelsEntry = Get-ZLevelsErrorEntry -LogPath $playerLog
        if ($zlevelsEntry) { Write-Host "First ERROR entry that names Z-Levels:`n$zlevelsEntry" } else { Write-Host 'No ERROR entry in Player.log names Z-Levels.' }
    }
    throw "Runtime scenarios did not all pass: $runDirectory"
}
$errors = @(Get-Content -LiteralPath (Join-Path $runDirectory 'Player.log') | Where-Object { $_ -match ' ERROR ' })
if ($ModsConfigSource) {
    # Other mods' errors are expected here. They go to a file for review. An error that involves Z-Levels code still fails the run.
    # Only error entries count. A Z-Levels frame in an ordinary log line is not an error. An entry runs until the next timestamped line.
    $ownErrors = @(foreach ($hit in Select-String -LiteralPath (Join-Path $runDirectory 'Player.log') -Pattern ' ERROR ' -Context 0,80) {
        $post = @($hit.Context.PostContext | ForEach-Object -Begin { $stop = $false } -Process { if ($stop) { return }; if ($_ -match '^\d{4}-\d{2}-\d{2} ') { $stop = $true; return }; $_ })
        if (((@($hit.Line) + $post) -join "`n") -match 'Z-?Levels') { $hit }
    })
    $errors | Set-Content -LiteralPath (Join-Path $runDirectory 'other-mod-errors.txt') -Encoding UTF8
    if ($ownErrors.Count) {
        $ownErrors | ForEach-Object { $_.Line }
        throw "Runtime log contains Z-Levels errors: $runDirectory"
    }
    Write-Host "Other mods logged $($errors.Count) error lines. See $(Join-Path $runDirectory 'other-mod-errors.txt')"
    Write-Host "Runtime validation passed with other mods. Evidence: $runDirectory"
    return
}
$relevantErrors = @($errors | Where-Object {
    $_ -notmatch 'Tried loading mod with the same packageId multiple times:' -or
    $_ -match 'multiple times: (ZLevels1[.]1|local[.]zlevels[.]validation|brrainz[.]harmony|ludeon[.]rimworld)[.]'
})
if ($relevantErrors.Count) {
    $relevantErrors
    throw "Runtime log contains errors: $runDirectory"
}
if ($errors.Count) { Write-Warning 'The log also contains unrelated installed-Workshop duplicate discovery errors; see the retained Player.log.' }
Write-Host "Runtime validation passed. Evidence: $runDirectory"
