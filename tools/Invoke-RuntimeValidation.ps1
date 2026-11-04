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
    [switch]$CoolingTwoNets,
    [switch]$RimatomicsPlusPlus,
    [switch]$SocialRelaxQueue,
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
# The labels a run must pass. Each one names the results it accepts: PASS for most labels, and PASS or TAKEN for a screenshot row, which reads
# TAKEN with the file it wrote. The scenario's own label always counts. A -FreeColonists run must record colonist work recorded, and a
# -OtherColonistsWork run must record other colonists work recorded. The core run writes each after its mining and building checks. A haul
# run ends before those checks and records the label as FAIL. A run with -RimatomicsPlusPlus must show the mod in its game log. A run with
# -Visual must write each of its screenshot rows. A -Visual run with cooling columns must also write its two calls rows, which read PASS when the planes
# drawn match the planes expected, with the cell and paired counts matching too. Its two partner tint rows, which Add-OutlineTintRows writes from the
# screenshots, must also read PASS.
function Get-RequiredLabels {
    param([string]$ScenarioLabel, [bool]$FreeColonists, [bool]$OtherColonistsWork, [bool]$RimatomicsPlusPlus, [bool]$Visual, [string[]]$ScreenshotLabels = @(), [bool]$CoolingColumns = $false, [bool]$CoolingNoColumns = $false, [bool]$CoolingTwoNets = $false)
    $labels = @([pscustomobject]@{ Label = $ScenarioLabel; Accept = 'PASS' })
    # A -Cooling run names its three link rows and its seven reactor rows, the last of which is the overheating row, so a missing row fails the gate by name. The cooling link row alone would hide which row is missing.
    if ($CoolingColumns) {
        $labels += [pscustomobject]@{ Label = "lower turbine shares the surface tower's loop"; Accept = 'PASS' }
        $labels += [pscustomobject]@{ Label = 'cooling link survives reload'; Accept = 'PASS' }
        $labels += [pscustomobject]@{ Label = 'cooling link breaks when a column is removed'; Accept = 'PASS' }
        $labels += [pscustomobject]@{ Label = 'cooling link breaks when a column is deconstructed'; Accept = 'PASS' }
        foreach ($reactorLabel in @('reactor runs on the lower level', 'loop ratio at or below 1', 'surface tower stays in the lower loop', 'no cooling alert', 'no meltdown', 'surface colonist not irradiated', 'cooled core stays below overheating')) {
            $labels += [pscustomobject]@{ Label = $reactorLabel; Accept = 'PASS' }
        }
    }
    # A -CoolingNoColumns run names its positive control row, which must read PASS.
    if ($CoolingNoColumns) { $labels += [pscustomobject]@{ Label = 'uncooled core overheats'; Accept = 'PASS' } }
    # A -CoolingTwoNets run names its four scene rows and the bridge row. The aggregate row is the scenario label.
    if ($CoolingTwoNets) {
        foreach ($twoNetsLabel in @('two nets scene', 'two nets: surface net A holds both towers once', 'two nets: surface net B holds both towers once', 'two nets: lower net holds both towers once', 'rimatomics bridge active')) {
            $labels += [pscustomobject]@{ Label = $twoNetsLabel; Accept = 'PASS' }
        }
    }
    if ($FreeColonists) { $labels += [pscustomobject]@{ Label = 'colonist work recorded'; Accept = 'PASS' } }
    if ($OtherColonistsWork) { $labels += [pscustomobject]@{ Label = 'other colonists work recorded'; Accept = 'PASS' } }
    if ($RimatomicsPlusPlus) { $labels += [pscustomobject]@{ Label = 'rimatomics++ mod active'; Accept = 'PASS' } }
    if ($Visual) {
        # A -Visual run with no screenshot labels would check nothing, so the function refuses to build that list.
        if (@($ScreenshotLabels).Count -eq 0) { throw '-Visual was given no screenshot labels, so its rows would not be checked.' }
        foreach ($shot in $ScreenshotLabels) { $labels += [pscustomobject]@{ Label = $shot; Accept = 'PASS or TAKEN' } }
        # The calls rows compare the planes of the draw calls, one for each cell, side and colour, with the planes the scenario expects, and they compare the
        # cell and paired counts too. One row is written at the amber stage, and one at the green stage after the lower column stands, and the green row must
        # draw a paired cell. The two tint rows are written from the partner screenshots after the game exits.
        if ($CoolingColumns) {
            $labels += [pscustomobject]@{ Label = 'partner highlight calls'; Accept = 'PASS with counts' }
            $labels += [pscustomobject]@{ Label = 'partner highlight calls with pair'; Accept = 'PASS with counts and a pair' }
            $labels += [pscustomobject]@{ Label = 'partner highlight tint amber'; Accept = 'PASS with values' }
            $labels += [pscustomobject]@{ Label = 'partner highlight tint green'; Accept = 'PASS with values' }
        }
    }
    return $labels
}
# A calls row reads PASS with three pairs of counts: the planes, the cells and the paired cells, each beside the count the scenario expected. The row passes
# only when each drawn count equals its expected count, and a row that needs a pair must draw at least one paired cell. This returns the problem, or
# nothing when the counts are sound.
function Get-CallsRowProblem {
    param([string]$Text, [bool]$NeedsPair)
    $counted = [regex]::Match($Text, '^PASS: ([0-9]+) planes match the ([0-9]+) expected planes, ([0-9]+) cells of ([0-9]+) expected cells, ([0-9]+) paired of ([0-9]+) expected paired$')
    if (!$counted.Success) { return 'the row does not read as PASS with its planes, cells and paired cells counted: ' + $Text }
    $planes = [int]$counted.Groups[1].Value
    $expectedPlanes = [int]$counted.Groups[2].Value
    $cells = [int]$counted.Groups[3].Value
    $expectedCells = [int]$counted.Groups[4].Value
    $paired = [int]$counted.Groups[5].Value
    $expectedPaired = [int]$counted.Groups[6].Value
    if ($planes -lt 1 -or $expectedPlanes -lt 1) { return 'no plane was drawn or expected' }
    if ($planes -ne $expectedPlanes) { return "$planes planes drawn, $expectedPlanes expected" }
    if ($cells -ne $expectedCells) { return "$cells cells drawn, $expectedCells expected" }
    if ($paired -ne $expectedPaired) { return "$paired paired cells drawn, $expectedPaired expected" }
    if ($NeedsPair -and $paired -lt 1) { return 'no paired cell was drawn, so the pair did not draw' }
    return $null
}
# The problems that fail a run. Any FAIL row fails it. A required label fails it unless a row records one of the results it accepts. A label
# that the runner observed itself counts by its observed value. No problem means the run passes.
function Get-GateProblems {
    param([string[]]$Results, $RequiredLabels, [hashtable]$Observed = @{})
    $problems = @()
    foreach ($row in @($Results | Where-Object { $_ -match '[|] FAIL:' })) { $problems += 'FAIL row: ' + $row }
    foreach ($required in $RequiredLabels) {
        $label = $required.Label
        if ($Observed.ContainsKey($label)) {
            if ($Observed[$label] -ne 'PASS') { $problems += 'required label not PASS: ' + $label + ' (' + $Observed[$label] + ')' }
            continue
        }
        # A screenshot row reads TAKEN and the path it wrote, so its text must start with TAKEN: and name a file. A calls row reads PASS with its counts, and
        # Get-CallsRowProblem checks each count after the match. A tint row reads PASS with the values it measured. The label must be the whole label, at the
        # start of the row or after "| ", so a longer label does not satisfy it.
        $accepted = switch ($required.Accept) { 'PASS or TAKEN' { '(PASS$|TAKEN: .+$)' } 'PASS with counts' { 'PASS: .+$' } 'PASS with counts and a pair' { 'PASS: .+$' } 'PASS with values' { 'PASS: .+$' } default { 'PASS$' } }
        $passed = @($Results | Where-Object { $_ -match ('(^|[|] )' + [regex]::Escape($label) + ' [|] ' + $accepted) })
        if ($passed.Count -eq 0) { $problems += 'required label missing or not ' + $required.Accept + ': ' + $label; continue }
        if ($required.Accept -like 'PASS with counts*') {
            $needsPair = $required.Accept -eq 'PASS with counts and a pair'
            foreach ($row in $passed) {
                $text = [regex]::Match($row, [regex]::Escape($label) + ' [|] (PASS: .+)$').Groups[1].Value
                $problem = Get-CallsRowProblem -Text $text -NeedsPair $needsPair
                if ($problem) { $problems += 'calls row not counted: ' + $label + ': ' + $problem }
            }
        }
    }
    return $problems
}
# The partner outline tint, read from the saved screenshots. Get-OutlineTintSettings holds every threshold and the sampled region, so the calibration
# sits in one place. The sampled region is the highlight cell: 15 by 15 pixels at x 632 to 646 and y 352 to 366 of a 1280 by 720 screenshot. A
# cooling column stands in the middle 7 by 7 block (x 636 to 642 and y 356 to 362), so that block is left out. The rest of the cell is sampled.
# The tint is measured against a baseline: the same ring BaselineShiftX pixels to the right, two cells east of the highlight cell. The look clears
# and lights the cells within two cells of the highlight cell, and no outline reaches the baseline, so the baseline shows the flat ground.
# Each channel is judged against the baseline, and each margin applies to its own channel, so a flat surface cannot pass on a sum of channels.
# Amber needs a red excess of at least AmberRedExcess, a green excess of at least AmberGreenExcess, and a red of at least AmberRedMin.
# Green needs its green excess minus its red excess to be at least GreenRelativeExcess. A green overlay raises green and lowers red, and a
# column can raise red, so the test takes the difference of the two excesses. Blue is not judged.
# Calibrated on the proof runs 20261102-230106, 20261102-233927, 20261102-234045 and 20261103-002454. Their amber cells have a red excess of 61.4
# and a green excess of 38.6. Their green cells have a green excess of 50.2 and a red excess of -7.6, so green minus red is 57.8. The failing runs
# 20261102-224947, 20261102-225711 and 20261103-001533 have amber red excesses of 10.2 and green excesses of 8.5. Their green cells have a green
# excess of 5.6 and a red excess of -3.0, so green minus red is 8.6. The baseline averages 46.3 in red and 48.1 in green in every run, so no outline
# reaches it. Its blue runs from 34 to 47 with the light.
function Get-OutlineTintSettings {
    return @{
        ImageWidth = 1280; ImageHeight = 720
        CellLeft = 632; CellTop = 352; CellRight = 646; CellBottom = 366
        ColumnLeft = 636; ColumnTop = 356; ColumnRight = 642; ColumnBottom = 362
        BaselineShiftX = 30
        AmberRedExcess = 40; AmberGreenExcess = 25; AmberRedMin = 70
        GreenRelativeExcess = 25
    }
}

# Reads one tint setting by name. A name that the settings table does not hold stops the run with a message that names it, so a misspelt name cannot
# read as null and pass for a setting.
function Get-TintSetting {
    param($Settings, [string]$Name)
    if (-not $Settings.ContainsKey($Name)) { throw ('The tint setting ' + $Name + ' is not in Get-OutlineTintSettings. Check the spelling of the name.') }
    return $Settings[$Name]
}

# A PNG starts with an eight byte signature and ends with its IEND chunk. A file without both is cut short, or it is not a PNG.
function Test-CompletePng {
    param($Bytes)
    $signature = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
    $trailer = [byte[]](0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82)
    if ($Bytes.Length -lt ($signature.Length + $trailer.Length)) { return $false }
    for ($i = 0; $i -lt $signature.Length; $i++) { if ($Bytes[$i] -ne $signature[$i]) { return $false } }
    $offset = $Bytes.Length - $trailer.Length
    for ($i = 0; $i -lt $trailer.Length; $i++) { if ($Bytes[$offset + $i] -ne $trailer[$i]) { return $false } }
    return $true
}

# Averages the ring of the cell that lies ShiftX pixels to the right of the highlight cell. The ring is the cell without its middle 7 by 7 block. The
# bounds are passed in, so the averages read no setting while the image is open.
function Get-RingAverage {
    param($Bitmap, [int]$Left, [int]$Top, [int]$Right, [int]$Bottom, [int]$ColumnLeft, [int]$ColumnTop, [int]$ColumnRight, [int]$ColumnBottom, [int]$ShiftX)
    $redTotal = 0; $greenTotal = 0; $blueTotal = 0; $count = 0
    for ($y = $Top; $y -le $Bottom; $y++) {
        for ($x = ($Left + $ShiftX); $x -le ($Right + $ShiftX); $x++) {
            if ($x -ge ($ColumnLeft + $ShiftX) -and $x -le ($ColumnRight + $ShiftX) -and $y -ge $ColumnTop -and $y -le $ColumnBottom) { continue }
            $pixel = $Bitmap.GetPixel($x, $y)
            $redTotal += $pixel.R; $greenTotal += $pixel.G; $blueTotal += $pixel.B; $count++
        }
    }
    return [pscustomobject]@{ Red = $redTotal / $count; Green = $greenTotal / $count; Blue = $blueTotal / $count; Count = $count }
}

# Returns PASS with the measured values when the highlight cell has the tint for its colour, judged per channel against its baseline. Otherwise it returns
# FAIL, naming the screenshot, the measured averages and the margins it missed.
# A file that is not a complete PNG, or that the decoder rejects, returns FAIL and names the file, so the gate and the summary still run.
function Get-OutlineTintResult {
    param([string]$Path, [ValidateSet('amber', 'green')][string]$Color)
    if (!(Test-Path -LiteralPath $Path)) { return 'FAIL: no screenshot file at ' + $Path }
    $settings = Get-OutlineTintSettings
    # Every setting is read by name before the image opens. A misspelt name stops the run with a message that names it, so it cannot read as null.
    $imageWidth = Get-TintSetting -Settings $settings -Name 'ImageWidth'
    $imageHeight = Get-TintSetting -Settings $settings -Name 'ImageHeight'
    $cellLeft = Get-TintSetting -Settings $settings -Name 'CellLeft'
    $cellTop = Get-TintSetting -Settings $settings -Name 'CellTop'
    $cellRight = Get-TintSetting -Settings $settings -Name 'CellRight'
    $cellBottom = Get-TintSetting -Settings $settings -Name 'CellBottom'
    $columnLeft = Get-TintSetting -Settings $settings -Name 'ColumnLeft'
    $columnTop = Get-TintSetting -Settings $settings -Name 'ColumnTop'
    $columnRight = Get-TintSetting -Settings $settings -Name 'ColumnRight'
    $columnBottom = Get-TintSetting -Settings $settings -Name 'ColumnBottom'
    $shiftX = Get-TintSetting -Settings $settings -Name 'BaselineShiftX'
    $amberRedExcess = Get-TintSetting -Settings $settings -Name 'AmberRedExcess'
    $amberGreenExcess = Get-TintSetting -Settings $settings -Name 'AmberGreenExcess'
    $amberRedMin = Get-TintSetting -Settings $settings -Name 'AmberRedMin'
    $greenRelativeExcess = Get-TintSetting -Settings $settings -Name 'GreenRelativeExcess'
    Add-Type -AssemblyName System.Drawing
    try {
        if (!(Test-CompletePng -Bytes ([System.IO.File]::ReadAllBytes($Path)))) {
            return 'FAIL: the screenshot ' + $Path + ' is not a complete PNG, so it cannot be read'
        }
        $bitmap = New-Object System.Drawing.Bitmap -ArgumentList $Path
        try {
            if ($bitmap.Width -ne $imageWidth -or $bitmap.Height -ne $imageHeight) {
                return 'FAIL: the screenshot is ' + $bitmap.Width + ' by ' + $bitmap.Height + ' pixels, not ' + $imageWidth + ' by ' + $imageHeight + ', so the sampled region does not apply'
            }
            $target = Get-RingAverage -Bitmap $bitmap -Left $cellLeft -Top $cellTop -Right $cellRight -Bottom $cellBottom -ColumnLeft $columnLeft -ColumnTop $columnTop -ColumnRight $columnRight -ColumnBottom $columnBottom -ShiftX 0
            $baseline = Get-RingAverage -Bitmap $bitmap -Left $cellLeft -Top $cellTop -Right $cellRight -Bottom $cellBottom -ColumnLeft $columnLeft -ColumnTop $columnTop -ColumnRight $columnRight -ColumnBottom $columnBottom -ShiftX $shiftX
        }
        finally { $bitmap.Dispose() }
    }
    catch {
        return 'FAIL: the screenshot ' + $Path + ' cannot be read as an image: ' + $_.Exception.Message
    }
    $average = (@($target.Red, $target.Green, $target.Blue) | ForEach-Object { $_.ToString('F1', [System.Globalization.CultureInfo]::InvariantCulture) }) -join ','
    $baselineText = (@($baseline.Red, $baseline.Green, $baseline.Blue) | ForEach-Object { $_.ToString('F1', [System.Globalization.CultureInfo]::InvariantCulture) }) -join ','
    Write-Host ('Partner tint for ' + $Color + ': average RGB ' + $average + ' over ' + $target.Count + ' pixels. Baseline RGB ' + $baselineText + ' over ' + $baseline.Count + ' pixels.')
    # Each channel is judged against the baseline, and each margin applies to its own channel, so a flat surface cannot pass on a sum of channels.
    # Green is judged by its excess minus the red excess, since a column can raise red as well as a green outline raising green.
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    $redExcess = $target.Red - $baseline.Red
    $greenExcess = $target.Green - $baseline.Green
    $redText = $redExcess.ToString('F1', $culture)
    $greenText = $greenExcess.ToString('F1', $culture)
    $measured = 'average RGB ' + $average + ' over baseline RGB ' + $baselineText
    if ($Color -eq 'amber') {
        if ($redExcess -ge $amberRedExcess -and $greenExcess -ge $amberGreenExcess -and $target.Red -ge $amberRedMin) {
            return 'PASS: ' + $measured + ', red excess ' + $redText + ', green excess ' + $greenText
        }
        return 'FAIL: the screenshot ' + $Path + ' cell has ' + $measured + ', which lacks the amber tint: red excess ' + $redText + ' needs ' + $amberRedExcess + ', green excess ' + $greenText + ' needs ' + $amberGreenExcess + ', red ' + $target.Red.ToString('F1', $culture) + ' needs ' + $amberRedMin
    }
    $relativeText = ($greenExcess - $redExcess).ToString('F1', $culture)
    if (($greenExcess - $redExcess) -ge $greenRelativeExcess) {
        return 'PASS: ' + $measured + ', green excess ' + $greenText + ', red excess ' + $redText + ', green minus red ' + $relativeText
    }
    return 'FAIL: the screenshot ' + $Path + ' cell has ' + $measured + ', which lacks the green tint: green minus red is ' + $relativeText + ' and needs ' + $greenRelativeExcess + ' (green excess ' + $greenText + ', red excess ' + $redText + ')'
}

# Appends the two partner tint verdicts to validation-results.txt as rows of their own, after the game exits. A tint row has no timestamp, so the log
# cross-check does not count it, and the gate reads it like the other rows. Each verdict names the screenshot it read.
function Add-OutlineTintRows {
    param([string]$ResultsPath, [string]$ShotFolder)
    $amber = Get-OutlineTintResult -Path (Join-Path $ShotFolder 'zlevels-partner-amber.png') -Color amber
    $green = Get-OutlineTintResult -Path (Join-Path $ShotFolder 'zlevels-partner-green.png') -Color green
    Add-Content -LiteralPath $ResultsPath -Value ('partner highlight tint amber | ' + $amber) -Encoding ASCII
    Add-Content -LiteralPath $ResultsPath -Value ('partner highlight tint green | ' + $green) -Encoding ASCII
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
    CoolingTwoNets = $CoolingTwoNets
    SocialRelaxQueue = $SocialRelaxQueue
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
$activeMods = @('brrainz.harmony', 'ludeon.rimworld', 'foxcosmic.zlevels17', 'local.zlevels.validation')
if ($ModsConfigSource) {
    [xml]$source = Get-Content -LiteralPath $ModsConfigSource -Raw -Encoding UTF8
    $activeMods = @($source.SelectNodes('/ModsConfigData/activeMods/li') | ForEach-Object { $_.InnerText } | Where-Object { $_ -and $_ -notin @('foxcosmic.zlevels17', 'local.zlevels.validation') })
    $activeMods += @('foxcosmic.zlevels17', 'local.zlevels.validation')
}
# The cooling runs need Dubs Rimatomics. Rimatomics++ depends on it, so -RimatomicsPlusPlus adds it too. Each mod goes before Z-Levels, unless the list already has it.
$rimatomicsMods = @()
if ($Cooling -or $CoolingNoColumns -or $CoolingTwoNets -or $RimatomicsPlusPlus) { $rimatomicsMods += 'dubwise.rimatomics' }
if ($RimatomicsPlusPlus) { $rimatomicsMods += 'nachotoast.rimatomicsplusplus' }
$rimatomicsMods = @($rimatomicsMods | Where-Object { $activeMods -notcontains $_ })
$activeMods = @($activeMods | ForEach-Object { if ($_ -eq 'foxcosmic.zlevels17') { $rimatomicsMods }; $_ })
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
if ($CoolingTwoNets) { $arguments += '-zlevelscoolingtwonets' }
if ($SocialRelaxQueue) { $arguments += '-zlevelssocialrelaxqueue' }
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
# The partner tint rows of a -Visual cooling run are written from its two partner screenshots after the cross-check, so the cross-check does not count them.
if ($Visual -and $Cooling -and -not $CoolingNoColumns) { Add-OutlineTintRows -ResultsPath $resultsPath -ShotFolder (Join-Path $runDirectory 'Screenshots') }
$results = Get-Content -LiteralPath $resultsPath
$requiredPass = if ($StairsDamage) { 'stairs damage' } elseif ($MultiBuild) { 'multiple builds below' } elseif ($UnreachableZone) { 'unreachable zone' } elseif ($OtherColonistHauls) { 'other colonist hauls across levels' } elseif ($AllColonistsHaul) { 'all colonists haul' } elseif ($InfestationGuard) { 'infestation guard' } elseif ($StaleLookup) { 'stale look-up map' } elseif ($VaultPlacement) { 'vault placement' } elseif ($VaultPocket) { 'vault pocket' } elseif ($VaultEnter) { 'vault build inside' } elseif ($VaultLoad) { 'vault load' } elseif ($VaultSave) { 'vault save' } elseif ($VaultInfestation) { 'vault infestation' } elseif ($Supply) { 'cross-level supply' } elseif ($Cooling -or $CoolingNoColumns) { 'cooling link' } elseif ($CoolingTwoNets) { 'two nets link' } elseif ($SocialRelaxQueue) { 'social relax queue' } else { 'core validation' }
$results
# The screenshot rows a -Visual run writes: four in the core run, and three in the cooling runs: the cooling column, then the partner highlight's amber and green.
$screenshotLabels = if ($Cooling -or $CoolingNoColumns) { @('cooling column screenshot', 'partner highlight amber screenshot', 'partner highlight green screenshot') } else { @('lower level screenshot', 'upper void screenshot', 'surface screenshot', 'surface hole screenshot') }
$requiredLabels = @(Get-RequiredLabels -ScenarioLabel $requiredPass -FreeColonists ([bool]$FreeColonists) -OtherColonistsWork ([bool]$OtherColonistsWork) -RimatomicsPlusPlus ([bool]$RimatomicsPlusPlus) -Visual ([bool]$Visual) -ScreenshotLabels $screenshotLabels -CoolingColumns ([bool]($Cooling -and -not $CoolingNoColumns)) -CoolingNoColumns ([bool]$CoolingNoColumns) -CoolingTwoNets ([bool]$CoolingTwoNets))
$observed = @{}
if ($RimatomicsPlusPlus) { $observed['rimatomics++ mod active'] = Get-RimatomicsObservation -LogPath (Join-Path $runDirectory 'Player.log') }
$gateProblems = @(Get-GateProblems -Results $results -RequiredLabels $requiredLabels -Observed $observed)
Write-Host ('Required labels for this run: ' + (@($requiredLabels | ForEach-Object { if ($_.Accept -eq 'PASS') { $_.Label } elseif ($_.Accept -eq 'PASS or TAKEN') { $_.Label + ' (PASS or TAKEN)' } else { $_.Label + ' (' + $_.Accept + ')' } }) -join ', ') + '.')
foreach ($label in $observed.Keys) { Write-Host "Observed by the runner: $label = $($observed[$label])." }
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
