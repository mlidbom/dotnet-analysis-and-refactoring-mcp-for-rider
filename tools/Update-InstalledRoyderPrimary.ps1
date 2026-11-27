#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory,
      [string]$SettingsFile = (Join-Path $env:LOCALAPPDATA 'DotNetAnalysisAndRefactoringMcp/primary/settings.json'),
      # A folder of tools builds for the private Rider to serve the newest of, such as the everyday Riders'. An update
      # keeps the one the settings name.
      [string]$ToolsFolder,
      # Where the primary has projects open, stop it gracefully first, which lets the calls in flight finish and closes
      # them, instead of refusing until they have closed by themselves. Their agents' next calls open them again, cold.
      [switch]$CloseOpenProjects)

$ErrorActionPreference = 'Stop'
$package = (Get-Item -LiteralPath $PackageDirectory).FullName
$settingsPath = (Get-Item -LiteralPath $SettingsFile).FullName
$installation = Split-Path -Parent $settingsPath
$original = [IO.File]::ReadAllText($settingsPath)
$current = $original | ConvertFrom-Json
$publicUrl = "http://127.0.0.1:$($current.port)/health"
$taskName = 'RoyderPrimary'

function Get-Listener {
   $socket = Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort $current.port -State Listen -ErrorAction SilentlyContinue
   if ($socket) { Get-Process -Id $socket.OwningProcess }
}

function Stop-Primary([string]$Executable) {
   # Its repeating trigger would start the stopped build again. Registering the task anew enables it.
   Disable-ScheduledTask -TaskName $taskName -TaskPath '\' | Out-Null
   $listener = Get-Listener
   if ($listener) {
      if ($listener.Path -ne $Executable) { throw "The public port belongs to $($listener.Path)." }
      $null = $listener.Handle
      & $newExecutable stop $settingsPath
      if ($LASTEXITCODE) { throw 'The running primary did not accept graceful shutdown. Its settings have been preserved.' }
      if (!$listener.WaitForExit(190000)) { throw 'The primary is still draining. Its settings have been preserved.' }
   }
   $until = [datetime]::UtcNow.AddSeconds(8)
   do {
      if ((Get-ScheduledTask -TaskName $taskName -TaskPath '\').State -ne 'Running') { return }
      Start-Sleep -Milliseconds 150
   } while ([datetime]::UtcNow -lt $until)
   if (Get-Listener) { throw 'The scheduled task still owns a public listener.' }
   Stop-ScheduledTask -TaskName $taskName
}

function Set-Configuration([string]$Json) {
   $temporary = $settingsPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
   try {
      [IO.File]::WriteAllText($temporary, $Json)
      [IO.File]::Move($temporary, $settingsPath, $true)
   } finally { [IO.File]::Delete($temporary) }
}

function Start-Primary([string]$Executable) {
   & "$PSScriptRoot/Register-RoyderPrimaryAtLogon.ps1" -PrimaryExecutable $Executable -SettingsFile $settingsPath | Out-Null
   Start-ScheduledTask -TaskName $taskName
   $until = [datetime]::UtcNow.AddSeconds(35)
   do {
      $listener = Get-Listener
      if ($listener) {
         if ($listener.Path -ne $Executable) { throw "Unexpected public listener: $($listener.Path)" }
         $null = Invoke-RestMethod $publicUrl -NoProxy -TimeoutSec 15
         return
      }
      Start-Sleep -Milliseconds 200
   } while ([datetime]::UtcNow -lt $until)
   throw "The scheduled primary did not start. Check $installation/logs."
}

$newExecutable = (Get-Item -LiteralPath (Join-Path $package 'Royder.Primary.exe')).FullName
$newCatalog = (Get-Item -LiteralPath (Join-Path $package 'catalog.json')).FullName
$assets = Join-Path $package RiderHost
$manifest = Get-Content -Raw -LiteralPath (Join-Path $assets package.json) | ConvertFrom-Json
$catalog = Get-Content -Raw -LiteralPath $newCatalog | ConvertFrom-Json
if ($catalog.build -cne $manifest.build -or $catalog.host -cne $manifest.hostHash) { throw 'The catalog and the staged Rider assets differ.' }

$task = Get-ScheduledTask -TaskName $taskName -TaskPath '\'
if ($task.State -notin 'Running','Ready') { throw 'Expected an enabled everyday primary task.' }
$oldExecutable = (Get-Item -LiteralPath (Join-Path (Split-Path -Parent $current.catalogFile) 'Royder.Primary.exe')).FullName
$oldListener = Get-Listener
if (($oldListener -and $oldListener.Path -ne $oldExecutable) -or (!$oldListener -and $task.State -eq 'Running')) {
   throw 'The public listener differs from the installed primary.'
}
if ($oldExecutable -eq $newExecutable) { throw 'Stage the updated primary in a new package directory.' }

# The private Rider runs nothing once its run has stopped or failed and no process of its job is left. Its journal's
# projects are then open nowhere. On 2026-11-24 a Rider update closed it, the old package could no longer start one, and
# every agent's call left an entry behind: the journal never emptied, and the run stayed 'Failed'.
# The primary and the private Rider's supervisor replace these records while this script reads them. A reader that
# doesn't share delete, as Get-Content's, keeps them from replacing it: a supervisor that couldn't publish its state
# failed a private Rider's start on 2026-11-27.
function Read-RiderRecord([string]$Path) {
   $stream = [IO.FileStream]::new($Path, 'Open', 'Read', [IO.FileShare]'ReadWrite,Delete')
   try { [IO.StreamReader]::new($stream).ReadToEnd() | ConvertFrom-Json } finally { $stream.Dispose() }
}
$runFile = Join-Path $current.rider.profileDirectory 'current.json'
function Test-PrivateRiderRuns {
   if (!(Test-Path -LiteralPath $runFile)) { return $false }
   $run = Read-RiderRecord $runFile
   $state = Read-RiderRecord (Join-Path $run 'state.json')
   $state.phase -notin 'Stopped','Failed' -or $state.jobRemaining.Count -or $state.survivors.Count
}

if ($CloseOpenProjects -and $oldListener) {
   # A graceful stop returns once the calls in flight are answered; the private Rider then closes its projects.
   try {
      Stop-Primary $oldExecutable
      $until = [datetime]::UtcNow.AddSeconds(120)
      while ((Test-PrivateRiderRuns) -and [datetime]::UtcNow -lt $until) { Start-Sleep -Seconds 2 }
   } finally { Enable-ScheduledTask -TaskName $taskName -TaskPath '\' | Out-Null }
   $oldListener = $null
}
$health = if ($oldListener) { Invoke-RestMethod $publicUrl -NoProxy -TimeoutSec 15 }
$riderRuns = Test-PrivateRiderRuns
$journal = Join-Path $current.rider.profileDirectory 'worktrees.json'
if (($health -and @($health.openingWorktrees).Count) -or
    ($riderRuns -and (Test-Path -LiteralPath $journal) -and @(Read-RiderRecord $journal).Count)) {
   throw 'The primary has managed projects open or opening. Switch after they close.'
}
if ($riderRuns) { throw 'The private Rider job has not drained.' }

$updated = $original | ConvertFrom-Json
$updated.catalogFile = $newCatalog
$updated.rider.assetsDirectory = $assets
$updated.rider.toolsBuild = $manifest.build
if ($ToolsFolder) { $updated.rider | Add-Member -Force -NotePropertyName toolsFolder -NotePropertyValue (Get-Item -LiteralPath $ToolsFolder).FullName }
$label = Split-Path -Leaf $package
$updated.rider.profileDirectory = Join-Path $installation ("everyday-state-" + $label)
$backup = Join-Path $installation ("settings.before-" + $label + "-" + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + ".json")
[IO.File]::Copy($settingsPath, $backup)
# A successful stop returns only after the primary has drained. Older releases can be stopped in a coordinated maintenance window first.
try {
   Stop-Primary $oldExecutable
   Set-Configuration ($updated | ConvertTo-Json -Depth 8)
   Start-Primary $newExecutable
} catch {
   $failure = $_.Exception.Message
   $remaining = Get-Listener
   if ($remaining -and $remaining.Path -eq $oldExecutable) {
      Enable-ScheduledTask -TaskName $taskName -TaskPath '\' | Out-Null
      throw "Update stopped before cutover: $failure. The previous executable still holds the public port. Previous settings: $backup"
   }
   try {
      Stop-Primary $newExecutable
      Set-Configuration $original
      Start-Primary $oldExecutable
   } catch { throw "Update failed: $failure. Rollback also failed: $($_.Exception.Message). The $taskName task stays disabled until it is registered again. Previous settings: $backup" }
   throw "Restored the previous primary after the update failed: $failure"
}
Write-Output "Active primary: $newExecutable on $($current.port). Previous settings: $backup."

# Each package's private Rider gets a profile of its own, about 2 GB of packages, caches and indexes. Only the new one
# runs, and the previous one is what a rollback to the previous settings uses: the others are deleted.
$kept = $updated.rider.profileDirectory, $current.rider.profileDirectory | ForEach-Object { [IO.Path]::GetFullPath($_) }
foreach ($profile in Get-ChildItem -LiteralPath $installation -Directory -Filter 'everyday-state-*') {
   if ($profile.FullName -in $kept) { continue }
   try { Remove-Item -LiteralPath $profile.FullName -Recurse -Force }
   catch { Write-Warning "The old profile $($profile.FullName) was not deleted: $($_.Exception.Message)" }
}
