#Requires -Version 7.0
<#
.SYNOPSIS
  Reads every BlueBird L:var in one call in the BBSTablet view and saves the JSON name -> value map.
.DESCRIPTION
  Runs tools/coherent-eval.ps1 (the Coherent debugger on port 19999) with snap.js, which make_snap_js.py generates.
  Read-only. MSFSBA must be closed: it holds the BBSTablet view, and Coherent allows one inspector socket per view
  ([DBG-9]).
.PARAMETER Out
  The file to write (one line of JSON).
.PARAMETER Js
  The snapshot expression. Defaults to snap.js beside this script.
.EXAMPLE
  pwsh -File snap.ps1 -Out before.json
#>
param(
  [Parameter(Mandatory = $true)][string]$Out,
  [string]$Js = ""
)
$ErrorActionPreference = "Stop"

function Fail([string]$message) {
  [Console]::Error.WriteLine("snap.ps1: $message")
  exit 1
}

$resolve = $ExecutionContext.SessionState.Path
if ($Js -eq "") { $Js = Join-Path $PSScriptRoot "snap.js" }
$Js = $resolve.GetUnresolvedProviderPathFromPSPath($Js)
$Out = $resolve.GetUnresolvedProviderPathFromPSPath($Out)
if (-not (Test-Path -LiteralPath $Js -PathType Leaf)) {
  Fail "no snapshot expression at $Js. Generate it first: python -I make_snap_js.py <BBS752.wasm> <lvars.tsv> snap.js"
}
$eval = Join-Path $PSScriptRoot "../../../../tools/coherent-eval.ps1"
if (-not (Test-Path -LiteralPath $eval -PathType Leaf)) { Fail "tools/coherent-eval.ps1 not found at $eval" }

$timer = [System.Diagnostics.Stopwatch]::StartNew()
$lines = @(& $eval -Title BBSTablet -ExprFile $Js 2>&1 | ForEach-Object { "$_" })
$timer.Stop()

# coherent-eval.ps1 prints "# resolved ..." first, then the JSON, or one line naming what went wrong.
$json = $lines | Where-Object { $_.TrimStart().StartsWith("{") } | Select-Object -Last 1
if (-not $json -or $json.Trim().Length -le 2) {
  $answer = ($lines | Where-Object { $_.Trim() -ne "" } | Select-Object -Last 1)
  if (-not $answer) { $answer = "nothing" }
  if ($json) { $answer = "an empty map (no variable could be read)" }
  Fail ("the Coherent debugger answered: " + $answer.Trim().TrimEnd(".") + ". " +
    "Is MSFS running with the BlueBird 757 and BBS Nest loaded, and MSFSBA closed? (port 19999, view BBSTablet, [DBG-9])")
}

$parent = Split-Path -Parent $Out
if ($parent -and -not (Test-Path -LiteralPath $parent)) { $null = New-Item -ItemType Directory -Path $parent }
[System.IO.File]::WriteAllText($Out, $json.Trim() + "`n", [System.Text.UTF8Encoding]::new($false))

$count = [regex]::Matches($json, '"[^"]+":').Count
Write-Output ("{0} variables in {1:N1} s -> {2}" -f $count, $timer.Elapsed.TotalSeconds, $Out)
