#requires -Version 7.4
<#
.SYNOPSIS
   Checks that the private Rider's launcher replaces a record, such as a run's state.json, while another process reads it.
.DESCRIPTION
   On 2026-11-27 the supervisor failed a private Rider's start: its Write-HostJson couldn't replace state.json for two
   seconds, "Access to the path is denied". File.Move can't replace a file any process has open, even one that shares
   delete, as every reader of these records does. Write-HostJson now renames with POSIX semantics (FileReplacement.cs).
   Exits 0 when it passes; prints what failed and exits 1 otherwise.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../src/Royder.Primary/Hosting/Launcher/Common.ps1')

$folder = [IO.Directory]::CreateDirectory((Join-Path ([IO.Path]::GetTempPath()) "record-replacement-$([guid]::NewGuid().ToString('N'))")).FullName
$failures = [Collections.Generic.List[string]]::new()
function Open-Reader([string]$Path, [IO.FileShare]$Share) { [IO.StreamReader]::new([IO.FileStream]::new($Path, 'Open', 'Read', $Share)) }
try {
   $record = Join-Path $folder state.json
   Write-HostJson $record @{ phase = 'Starting' }

   $sharingDelete = Open-Reader $record ([IO.FileShare]'ReadWrite,Delete')
   try {
      $timer = [Diagnostics.Stopwatch]::StartNew()
      Write-HostJson $record @{ phase = 'Running' }
      if ($timer.ElapsedMilliseconds -gt 500) { $failures.Add("Replacing it while a reader that shares delete has it open took $($timer.ElapsedMilliseconds) ms: it waited for the reader.") }
      if ((Read-HostJson $record).phase -ne 'Running') { $failures.Add('A new reader reads the old record after it was replaced.') }
      if (($sharingDelete.ReadToEnd() | ConvertFrom-Json).phase -ne 'Starting') { $failures.Add('The reader that had it open no longer reads the copy it opened.') }
   } finally { $sharingDelete.Dispose() }

   $notSharingDelete = Open-Reader $record ([IO.FileShare]'Read')
   try {
      $refused = $false
      try { Write-HostJson $record @{ phase = 'Stopped' } } catch { $refused = $true }
      if (!$refused) { $failures.Add('It replaced the record while a reader that doesn''t share delete had it open: the check of that limit no longer holds.') }
   } finally { $notSharingDelete.Dispose() }

   if (@(Get-ChildItem -LiteralPath $folder -Filter '*.tmp').Count) { $failures.Add('A pending copy was left behind.') }
} finally { Remove-Item -LiteralPath $folder -Recurse -Force }

if ($failures.Count) { $failures | ForEach-Object { Write-Output "FAIL: $_" }; exit 1 }
Write-Output 'PASS: records are replaced while readers that share delete have them open.'
