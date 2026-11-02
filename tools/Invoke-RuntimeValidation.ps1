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
# A run starts one scenario, or the core validation run when no scenario switch is set. A switch that the chosen run does not read would be
# ignored, so the check refuses it before anything starts. The reads are in $switchReaders, which lists them from RuntimeValidation.cs.
function Assert-SwitchesRead {
    param(
        [System.Collections.IDictionary]$Scenarios,
        [System.Collections.IDictionary]$Modifiers,
        [System.Collections.IDictionary]$Readers
    )
    $started = @($Scenarios.Keys | Where-Object { [bool]$Scenarios[$_] })
    if ($started.Count -gt 1) {
        throw "A run takes one scenario switch, but these are set: $(($started | ForEach-Object { '-' + $_ }) -join ', '). Start a separate run for each scenario. No run was started."
    }
    $chosen = if ($started.Count -eq 1) { $started[0] } else { 'Core' }
    $chosenName = if ($started.Count -eq 1) { '-' + $chosen } else { 'the core validation run' }
    $set = @($Modifiers.Keys | Where-Object { [bool]$Modifiers[$_] })
    # AllColonistsHaul and OtherColonistHauls each set a required label, and the run checks one label. Two of them would leave one label unchecked.
    if (@($set | Where-Object { $_ -in @('AllColonistsHaul', 'OtherColonistHauls') }).Count -gt 1) {
        throw "-AllColonistsHaul and -OtherColonistHauls each set a required label, and a run checks one label. Run them separately. No run was started."
    }
    foreach ($name in $set) {
        $readBy = @($Readers[$name])
        if (($readBy -contains 'Every run') -or ($readBy -contains $chosen)) { continue }
        $readerList = @($readBy | ForEach-Object { if ($_ -eq 'Core') { 'the core validation run' } else { '-' + $_ } }) -join ', '
        # -Visual changes the run even where no screenshot is taken, because it opens the game in a window. The message says that, instead of
        # saying the switch is ignored.
        $effect = if ($name -eq 'Visual') { 'would open a window and take no screenshots' } else { 'would ignore it' }
        throw "The switch -$name is not read by $chosenName, so that run $effect. It is read by $readerList. Remove -$name, or run a scenario that reads it. No run was started."
    }
}
# The labels a run must pass. The scenario's own label always counts. A run with a work switch must also record the work label, which the core
# run writes after its mining and building checks. The haul run ends before those checks and records the label as FAIL. A run with
# -RimatomicsPlusPlus must show the mod in its game log.
function Get-RequiredLabels {
    param([string]$ScenarioLabel, [bool]$FreeColonists, [bool]$OtherColonistsWork, [bool]$RimatomicsPlusPlus)
    $labels = @($ScenarioLabel)
    if ($FreeColonists -or $OtherColonistsWork) { $labels += 'other colonists work recorded' }
    if ($RimatomicsPlusPlus) { $labels += 'rimatomics++ mod active' }
    return $labels
}
# The problems that fail a run. Any FAIL row fails it. A required label fails it unless a row records PASS for it. A label that the runner
# observed itself counts by its observed value. No problem means the run passes.
function Get-GateProblems {
    param([string[]]$Results, [string[]]$RequiredLabels, [hashtable]$Observed = @{})
    $problems = @()
    foreach ($row in @($Results | Where-Object { $_ -match '[|] FAIL:' })) { $problems += 'FAIL row: ' + $row }
    foreach ($label in $RequiredLabels) {
        if ($Observed.ContainsKey($label)) {
            if ($Observed[$label] -ne 'PASS') { $problems += 'required label not PASS: ' + $label + ' (' + $Observed[$label] + ')' }
            continue
        }
        $passed = @($Results | Where-Object { $_ -match ('[|] ' + [regex]::Escape($label) + ' [|] PASS$') })
        if ($passed.Count -eq 0) { $problems += 'required label missing or not PASS: ' + $label }
    }
    return $problems
}
# The mod check reads the game log. RimWorld writes the mod list when it starts a new game, so the list after that line is the set of mods the
# game ran with. The check passes only when that list names the mod.
function Get-RimatomicsObservation {
    param([string]$LogPath)
    if (!(Test-Path -LiteralPath $LogPath)) { return 'FAIL: the game log is missing, so the mod list cannot be read' }
    $lines = @(Get-Content -LiteralPath $LogPath)
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'Initializing new game with mods:') { $start = $i; break }
    }
    if ($start -lt 0) { return 'FAIL: the game log has no new-game mod list' }
    # An entry can carry a note, such as "(incompatible version)". A note means the game did not load that mod, so the check fails on it.
    $entries = @()
    for ($j = $start + 1; $j -lt $lines.Count; $j++) {
        $item = [regex]::Match($lines[$j], '^\s+-\s+(\S+)(.*)$')
        if (!$item.Success) { break }
        $entries += [pscustomobject]@{ Id = $item.Groups[1].Value; Note = $item.Groups[2].Value.Trim() }
    }
    $mine = @($entries | Where-Object { $_.Id -match '(?i)^nachotoast\.rimatomicsplusplus(\.steam)?$' })
    if ($mine.Count -eq 0) { return 'FAIL: nachotoast.rimatomicsplusplus is not in the new-game mod list' }
    if ($mine[0].Note -ne '') { return 'FAIL: nachotoast.rimatomicsplusplus is listed but not loaded: ' + $mine[0].Note }
    return 'PASS'
}
# The scenario switches. Each one starts its own scenario, and the harness dispatch returns after the first one it finds.
$scenarioSwitches = [ordered]@{
    InfestationGuard = $InfestationGuard
    StaleLookup = $StaleLookup
    VaultPlacement = $VaultPlacement
    VaultPocket = $VaultPocket
    VaultEnter = $VaultEnter
    VaultLoad = $VaultLoad
    VaultSave = $VaultSave
    VaultInfestation = $VaultInfestation
    Supply = $Supply
    UnreachableZone = $UnreachableZone
    MultiBuild = $MultiBuild
    StairsDamage = $StairsDamage
    Cooling = $Cooling
    CoolingNoColumns = $CoolingNoColumns
}
# The other switches. Each one is read only by the runs listed for it in $switchReaders. 'Core' is the run with no scenario switch.
$modifierSwitches = [ordered]@{
    Visual = $Visual
    AllColonistsHaul = $AllColonistsHaul
    OtherColonistHauls = $OtherColonistHauls
    FreeColonists = $FreeColonists
    OtherColonistsWork = $OtherColonistsWork
    MultiBuildLowerLogs = $MultiBuildLowerLogs
    RimatomicsPlusPlus = $RimatomicsPlusPlus
    ZLevelsDebug = $ZLevelsDebug
}
# The reads, from RuntimeValidation.cs. 'Every run' marks a switch that the launch, the mod list, or a check before the dispatch applies to every run.
$switchReaders = [ordered]@{
    Visual = @('Core', 'Cooling', 'CoolingNoColumns')
    AllColonistsHaul = @('Core')
    OtherColonistHauls = @('Core')
    FreeColonists = @('Core')
    OtherColonistsWork = @('Core')
    MultiBuildLowerLogs = @('MultiBuild')
    RimatomicsPlusPlus = @('Every run')
    ZLevelsDebug = @('Every run')
}
$null = Assert-SwitchesRead -Scenarios $scenarioSwitches -Modifiers $modifierSwitches -Readers $switchReaders
# One streaming pass over the game log, for the cause hint and the Z-Levels gate. An ERROR entry starts at any line with the whole word ERROR,
# matched case-sensitively, timestamped or not. It ends at the next timestamped line or at the next ERROR line. Only the entry count, the count
# of entries that mention Z-Levels, the first of those entries, and the first Z-Levels WARN line are kept. The log is read as a stream.
function Get-ErrorScan {
    param([string]$LogPath)
    $errorTag = [regex]::new('\bERROR\b')
    # A WARN line has WARN in the level field after its timestamp, the place ERROR occupies in a timestamped line.
    $warnTag = [regex]::new('^\d{4}-\d{2}-\d{2} \S+\s+WARN\b')
    $stamp = [regex]::new('^\d{4}-\d{2}-\d{2} ')
    $zlevels = [regex]::new('Z-?Levels', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $entries = 0
    $zlevelsEntries = 0
    $first = $null
    $firstWarn = $null
    $lines = New-Object System.Collections.Generic.List[string]
    $inEntry = $false
    $entryHasZLevels = $false
    foreach ($line in [System.IO.File]::ReadLines($LogPath)) {
        if ($null -eq $firstWarn -and $warnTag.IsMatch($line) -and $zlevels.IsMatch($line)) { $firstWarn = $line }
        $startsEntry = $errorTag.IsMatch($line)
        if ($inEntry -and ($startsEntry -or $stamp.IsMatch($line))) {
            $entries++
            if ($entryHasZLevels) {
                $zlevelsEntries++
                if ($null -eq $first) { $first = $lines -join "`n" }
            }
            $lines.Clear()
            $inEntry = $false
            $entryHasZLevels = $false
        }
        if ($startsEntry) { $inEntry = $true }
        if ($inEntry) {
            # Lines are kept only until the first Z-Levels entry is found, because only that entry is printed.
            if ($null -eq $first) { $lines.Add($line) }
            if ($zlevels.IsMatch($line)) { $entryHasZLevels = $true }
        }
    }
    if ($inEntry) {
        $entries++
        if ($entryHasZLevels) {
            $zlevelsEntries++
            if ($null -eq $first) { $first = $lines -join "`n" }
        }
    }
    return [pscustomobject]@{ Entries = $entries; ZLevelsEntries = $zlevelsEntries; FirstZLevelsEntry = $first; FirstZLevelsWarn = $firstWarn }
}
# Each validation Record call writes one timestamped row to validation-results.txt, then one "[ZLevels validation] " line to Player.log.
# This counts both, leaving out the log copy rows, and throws when the counts differ, so a row or a marker that failed to write fails the run.
# It reads only the two files. Nothing in process is consulted.
function Assert-ResultsMatchLog {
    param([string]$ResultsPath, [string]$LogPath)
    $resultRows = 0
    foreach ($line in [System.IO.File]::ReadLines($ResultsPath)) {
        if ($line -match '^\d{4}-\d{2}-\d{2}T\S+ [|] (?!log copy [|] )') { $resultRows++ }
    }
    $markerLines = 0
    foreach ($line in [System.IO.File]::ReadLines($LogPath)) {
        if ($line.Contains('[ZLevels validation] ')) { $markerLines++ }
    }
    if ($resultRows -ne $markerLines) {
        # A log copy row says that a Record call could not write its marker. The rows print before the throw, so the cause is on screen beside the counts.
        foreach ($line in [System.IO.File]::ReadLines($ResultsPath)) {
            if ($line -match '^\S+ [|] log copy [|] ') { Write-Host $line }
        }
        throw "The results file and the game log disagree. validation-results.txt has $resultRows result rows, not counting log copy rows, and Player.log has $markerLines [ZLevels validation] lines: $(Split-Path -Parent $ResultsPath)"
    }
    return [pscustomobject]@{ ResultRows = $resultRows; MarkerLines = $markerLines }
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
    # The game log is copied as bytes, and the copy must hash the same as its source. A text copy would change the bytes.
    $runLog = Join-Path $runDirectory 'Player.log'
    try {
        Copy-Item -LiteralPath $log -Destination $runLog -Force
        if ((Get-FileHash -LiteralPath $log -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $runLog -Algorithm SHA256).Hash) {
            throw "The copy of the game log does not match its source: $runDirectory"
        }
    }
    catch {
        throw "Copying the game log failed. Source: $log. Destination: $runLog. Run folder: $runDirectory. Cause: $($_.Exception.Message)"
    }
}
else {
    throw "The game log is missing at $log, so this run has no Player.log copy: $runDirectory"
}
$resultsPath = Join-Path $runDirectory 'validation-results.txt'
if (!(Test-Path -LiteralPath $resultsPath)) { throw "Game exited without validation results: $runDirectory" }
# The result rows and the game log's marker lines must agree. This reads only the two files, so it applies to every run.
$cross = Assert-ResultsMatchLog -ResultsPath $resultsPath -LogPath (Join-Path $runDirectory 'Player.log')
Write-Host "Result rows: $($cross.ResultRows). [ZLevels validation] lines in Player.log: $($cross.MarkerLines)."
$results = Get-Content -LiteralPath $resultsPath
$requiredPass = if ($StairsDamage) { 'stairs damage' } elseif ($MultiBuild) { 'multiple builds below' } elseif ($UnreachableZone) { 'unreachable zone' } elseif ($OtherColonistHauls) { 'other colonist hauls across levels' } elseif ($AllColonistsHaul) { 'all colonists haul' } elseif ($InfestationGuard) { 'infestation guard' } elseif ($StaleLookup) { 'stale look-up map' } elseif ($VaultPlacement) { 'vault placement' } elseif ($VaultPocket) { 'vault pocket' } elseif ($VaultEnter) { 'vault build inside' } elseif ($VaultLoad) { 'vault load' } elseif ($VaultSave) { 'vault save' } elseif ($VaultInfestation) { 'vault infestation' } elseif ($Supply) { 'cross-level supply' } elseif ($Cooling -or $CoolingNoColumns) { 'cooling link' } else { 'core validation' }
$results
$requiredLabels = @(Get-RequiredLabels -ScenarioLabel $requiredPass -FreeColonists ([bool]$FreeColonists) -OtherColonistsWork ([bool]$OtherColonistsWork) -RimatomicsPlusPlus ([bool]$RimatomicsPlusPlus))
$observed = @{}
if ($RimatomicsPlusPlus) { $observed['rimatomics++ mod active'] = Get-RimatomicsObservation -LogPath (Join-Path $runDirectory 'Player.log') }
$gateProblems = @(Get-GateProblems -Results $results -RequiredLabels $requiredLabels -Observed $observed)
Write-Host ('Required labels for this run: ' + ($requiredLabels -join ', ') + '.')
foreach ($label in $observed.Keys) { Write-Host "Observed from Player.log: $label = $($observed[$label])." }
if ($gateProblems.Count -gt 0) {
    # The cause hint: the Z-Levels ERROR entries in the run's copy of the game log, with their count, so a failing run names its cause.
    # Earlier entries in this log may be unrelated to this run, so the hint says so.
    $playerLog = Join-Path $runDirectory 'Player.log'
    if (!(Test-Path -LiteralPath $playerLog)) {
        Write-Host 'No Player.log in the run folder.'
    }
    else {
        $scan = Get-ErrorScan -LogPath $playerLog
        if ($scan.ZLevelsEntries) {
            Write-Host "Z-Levels ERROR entries in Player.log: $($scan.ZLevelsEntries). Earlier entries in this log may be unrelated to this run. The first one follows.`n$($scan.FirstZLevelsEntry)"
        }
        else {
            Write-Host 'No ERROR entry in Player.log names Z-Levels.'
            if ($scan.FirstZLevelsWarn) {
                Write-Host "First Z-Levels WARN line in Player.log. Earlier lines in this log may be unrelated to this run.`n$($scan.FirstZLevelsWarn)"
            }
        }
    }
    Write-Host ('Gate problems: ' + ($gateProblems -join '; ') + '.')
    throw "Runtime scenarios did not all pass: $runDirectory"
}
# The pass gate's line test is the 1369ed4 test. The whole-word ERROR rule belongs to Get-ErrorScan only.
$errors = @(Get-Content -LiteralPath (Join-Path $runDirectory 'Player.log') | Where-Object { $_ -match ' ERROR ' })
if ($ModsConfigSource) {
    # Other mods' errors are expected here. They go to a file for review. An error that involves Z-Levels code still fails the run.
    # Only error entries count, read by the same scan as the cause hint. A Z-Levels frame in an ordinary log line is not an error.
    $scan = Get-ErrorScan -LogPath (Join-Path $runDirectory 'Player.log')
    $errors | Set-Content -LiteralPath (Join-Path $runDirectory 'other-mod-errors.txt') -Encoding UTF8
    if ($scan.ZLevelsEntries) {
        Write-Host "Z-Levels ERROR entries in Player.log: $($scan.ZLevelsEntries). The first one follows.`n$($scan.FirstZLevelsEntry)"
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
