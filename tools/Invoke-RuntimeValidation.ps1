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
    [switch]$ZLevelsDebug,
    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
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
    <li>brrainz.harmony</li>
    <li>ludeon.rimworld</li>
    <li>zlevels1.1</li>
    <li>local.zlevels.validation</li>
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
$configuration | Set-Content -LiteralPath (Join-Path $configDirectory 'ModsConfig.xml') -Encoding UTF8
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
if ($ZLevelsDebug) { $arguments += '-zlevelsdebug' }
$process = Start-Process -FilePath $executable -WorkingDirectory $game -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id
    throw "Validation timed out; isolated files are in $runDirectory"
}

$log = Join-Path $env:USERPROFILE 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log'
if (Test-Path -LiteralPath $log) {
    Get-Content -LiteralPath $log | Set-Content -LiteralPath (Join-Path $runDirectory 'Player.log') -Encoding UTF8
}
$resultsPath = Join-Path $runDirectory 'validation-results.txt'
if (!(Test-Path -LiteralPath $resultsPath)) { throw "Game exited without validation results: $runDirectory" }
$results = Get-Content -LiteralPath $resultsPath
$requiredPass = if ($OtherColonistHauls) { 'other colonist hauls across levels' } elseif ($AllColonistsHaul) { 'all colonists haul' } elseif ($InfestationGuard) { 'infestation guard' } elseif ($StaleLookup) { 'stale look-up map' } elseif ($VaultPlacement) { 'vault placement' } elseif ($VaultPocket) { 'vault pocket' } elseif ($VaultEnter) { 'vault build inside' } elseif ($VaultLoad) { 'vault load' } elseif ($VaultSave) { 'vault save' } elseif ($VaultInfestation) { 'vault infestation' } elseif ($Supply) { 'cross-level supply' } else { 'core validation' }
$results
if ($results -match '[|] FAIL:' -or !($results -match ('[|] ' + $requiredPass + ' [|] PASS$'))) {
    throw "Runtime scenarios did not all pass: $runDirectory"
}
$errors = @(Get-Content -LiteralPath (Join-Path $runDirectory 'Player.log') | Where-Object { $_ -match ' ERROR ' })
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
