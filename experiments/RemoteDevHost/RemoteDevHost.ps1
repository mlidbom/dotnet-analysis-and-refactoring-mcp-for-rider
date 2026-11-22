#requires -Version 7.4
[CmdletBinding()]
param(
   [Parameter(Mandatory, Position=0)][ValidateSet('start','status','stop','audit')][string]$Command,
   [Parameter(Mandatory)][string]$Profile,
   [string]$Solution,
   [ValidateRange(1,65535)][int]$RemoteDevPort,
   [ValidateRange(1,65526)][int]$RoyderPort,
   [ValidateRange(128,131072)][int]$HeapMiB = 768,
   [string]$ToolsBuild,
   [string[]]$ExtraPlugin = @(),
   [switch]$IsolateProjectStores,
   [hashtable]$ExtraEnvironment = @{},
   [string]$ManagedAssets,
   [string]$RiderHome = (Join-Path $env:LOCALAPPDATA 'Programs/Rider'),
   [ValidateSet('Before','After')][string]$Phase
)
. "$PSScriptRoot/Common.ps1"

function Wait-ForStop($Run, $Supervisor) {
   $until = [datetime]::UtcNow.AddSeconds(50)
   do {
      $state = Get-RunState $Run
      if ($state -and $state.phase -in 'Stopped','Failed') {
         return $state
      }
      if ($Supervisor.HasExited) { throw "Supervisor exited without a shutdown report. See $Run." }
      Start-Sleep -Milliseconds 250
   } while ([datetime]::UtcNow -lt $until)
   throw "The supervisor has not finished shutdown. See $Run."
}

function Get-IndexedHealth($Plan) {
   $base = "http://127.0.0.1:$($Plan.royderPort)"
   try {
      $health = Invoke-RestMethod "$base/health" -NoProxy -TimeoutSec 2
      $solutions = @($health.solutions | Where-Object path -eq $Plan.solution)
      if ($solutions.Count -ne 1) { return }
      $solution = $solutions[0]
      if (!$solution.PSObject.Properties['tools'] -or $solution.indexing) { return }
      $tools = $solution.tools
      if ($tools.build -ne $Plan.build -or $tools.host -ne $Plan.hostHash -or $tools.folder -ne $Plan.tools) { return }
      $reply = Invoke-RestMethod "$base/" -NoProxy -TimeoutSec 2 -Method Post -ContentType 'application/json' -Body '{"jsonrpc":"2.0","id":1,"method":"internal/status","params":{}}'
      if ($reply.result.role -eq 'primary' -and $reply.result.port -eq $Plan.royderPort) { return $health }
   } catch [System.Net.Http.HttpRequestException] { return }
   catch [System.OperationCanceledException] { return }
}

function Show-Host($Run, $State) {
   $plan = Read-HostJson (Join-Path $Run plan.json)
   $live = @($State.members | Where-Object { Get-LiveIdentity $_ })
   $supervisor = Get-LiveIdentity $State.supervisor
   $phaseName = $State.phase
   if (!$supervisor -and $phaseName -in 'Starting','Running','Stopping') { $phaseName = 'Supervisor exited' }
   $indexed = $false
   if ($phaseName -eq 'Running' -and $State.hostProcess -and (Get-LiveIdentity $State.hostProcess)) {
      $indexed = [bool](Get-IndexedHealth $plan)
   }
   [pscustomobject]@{ phase=$phaseName; indexed=$indexed; solution=$plan.solution; toolsBuild=$plan.build
      javaPid=$(if ($State.hostProcess) { $State.hostProcess.pid }); processIds=@($live | ForEach-Object { $_.pid })
      remoteDevPort=$plan.remoteDevPort; royderPort=$plan.royderPort; terminated=$State.terminated
      evidence=$Run; error=$State.error }
}

$profilePath = Get-ProfilePath $Profile -Managed:([bool]$ManagedAssets)
if ($Command -in 'start','audit') { [IO.Directory]::CreateDirectory($profilePath) | Out-Null }
$current = Join-Path $profilePath current.json
if ($Command -eq 'status') {
   if (!(Test-Path -LiteralPath $current)) { return [pscustomobject]@{phase='Never started';profile=$profilePath} }
   $run = Read-HostJson $current
   $state = Get-RunState $run
   if (!$state) { return [pscustomobject]@{phase='Starting';evidence=$run} }
   return Show-Host $run $state
}
if ($Command -eq 'stop' -and !(Test-Path -LiteralPath $current)) { return [pscustomobject]@{phase='Never started';profile=$profilePath} }

# Serialize commands for this profile; each supervisor independently reserves its entire port range.
$lock = [IO.File]::Open((Join-Path $profilePath commands.lock), 'OpenOrCreate','ReadWrite','None')
try {
   if ($Command -eq 'audit') {
      if (!$Phase) { throw 'Specify -Phase Before or -Phase After.' }
      . "$PSScriptRoot/ProfileAudit.ps1"
      return Invoke-ProfileAudit $profilePath $RiderHome $Phase
   }
   if (Test-Path -LiteralPath $current) {
      $run = Read-HostJson $current
      $state = Get-RunState $run
      $supervisor = if ($state) { Get-LiveIdentity $state.supervisor }
      if ($Command -eq 'stop') {
         if (!$state) { throw "No supervisor state in $run." }
         if ($state.phase -notin 'Stopped','Failed') {
            if (!$supervisor) { throw "Supervisor disappeared. Check the last recorded process identities in $run." }
            Request-HostStop $run
            $state = Wait-ForStop $run $supervisor
         }
         if ($state.jobRemaining.Count -or $state.survivors.Count -or $state.listeningAfter.Count) {
            throw "Cleanup verification failed. See $run/state.json."
         }
         return Show-Host $run $state
      }
      if ($supervisor -and $state.phase -notin 'Stopped','Failed') { throw "This profile already has a supervisor (PID $($supervisor.Id))." }
   }
   if (!$Solution -or !$ToolsBuild -or !$RemoteDevPort -or !$RoyderPort) { throw 'Start requires -Solution, -ToolsBuild, -RemoteDevPort and -RoyderPort.' }
   . "$PSScriptRoot/HostProfile.ps1"
   $run = [IO.Directory]::CreateDirectory((Join-Path $profilePath "runs/$([guid]::NewGuid().ToString('N'))")).FullName
   $plan = New-HostPlan $profilePath $run $Solution $RiderHome $ToolsBuild $RemoteDevPort $RoyderPort $HeapMiB $ExtraPlugin $ManagedAssets $IsolateProjectStores.IsPresent
   foreach ($key in $ExtraEnvironment.Keys) {
      if ($plan.environment.ContainsKey($key) -or $key -in 'JAVA_TOOL_OPTIONS','JDK_JAVA_OPTIONS','_JAVA_OPTIONS') {
         throw "$key is set by the private launch profile."
      }
   }
   Write-HostJson (Join-Path $run plan.json) $plan
   Write-HostJson $current $run
   $worker = Start-Process (Join-Path $PSHOME pwsh.exe) -WindowStyle Hidden -PassThru -WorkingDirectory $run -Environment $ExtraEnvironment -ArgumentList @(
      '-NoLogo','-NoProfile','-File', ('"' + "$PSScriptRoot/Supervise.ps1" + '"'), '-Run', ('"' + $run + '"')
   ) -RedirectStandardOutput (Join-Path $run supervisor.stdout) -RedirectStandardError (Join-Path $run supervisor.stderr)
   try {
      $until = [datetime]::UtcNow.AddMinutes(4)
      do {
         $state = Get-RunState $run
         if ($state -and $state.error) { throw $state.error }
         if ($worker.HasExited) { throw "Supervisor exited. See $run/supervisor.stderr." }
         if ($state -and $state.phase -eq 'Running') {
            if ($plan.ContainsKey('projectStores')) {
               # The first real solution must go through the management plugin just like every later open.
               $builtIn = Get-AdvertisedPort $run -IfStarted
               if ($builtIn) {
                  $listeners = @(Get-NetTCPConnection -State Listen -LocalPort @($builtIn, $RemoteDevPort) -ErrorAction SilentlyContinue |
                     Where-Object OwningProcess -eq $state.hostProcess.pid | Select-Object -ExpandProperty LocalPort -Unique)
                  if ($listeners.Count -eq 2) { return Show-Host $run $state }
               }
            } else {
               $health = Get-IndexedHealth $plan
               if ($health) {
                  $rd = @(Get-NetTCPConnection -State Listen -LocalPort $RemoteDevPort -ErrorAction SilentlyContinue)
                  if (@($rd | ForEach-Object OwningProcess) -contains $state.hostProcess.pid) {
                     Write-HostJson (Join-Path $run indexed-health.json) $health
                     return Show-Host $run (Get-RunState $run)
                  }
               }
            }
         }
         Start-Sleep -Milliseconds 350
      } while ([datetime]::UtcNow -lt $until)
      throw "Timed out waiting for the private Rider to become ready. See $run/log."
   } catch {
      if (!$worker.HasExited) {
         if ($ManagedAssets) { [IO.File]::WriteAllText((Join-Path $run 'abandon.request'), '') }
         else { Request-HostStop $run }
         $null = Wait-ForStop $run $worker
      }
      throw
   }
} finally { $lock.Dispose() }
