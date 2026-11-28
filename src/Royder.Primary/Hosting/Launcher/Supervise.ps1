param([Parameter(Mandatory)][string]$Run)
. "$PSScriptRoot/Common.ps1"

$plan = Read-HostJson (Join-Path $Run plan.json)
$observed = @{}
$portLocks = @()
$job = $null
$activeProfile = $null
$state = @{ phase='Starting'; supervisor=Get-ProcessIdentity (Get-Process -Id $PID); hostProcess=$null; members=@()
            error=$null; terminated=@(); survivors=@(); jobRemaining=@(); listeningAfter=@(); exitCode=$null }
function Publish-State { Write-HostJson (Join-Path $Run state.json) $state }

try {
   Publish-State
   $activeProfile = [IO.File]::Open((Join-Path $plan.profile host.lock), 'OpenOrCreate','ReadWrite','None')
   Add-Type -Path "$PSScriptRoot/WindowsJob.cs"
   $job = [Royder.Primary.Hosting.Launcher.WindowsJob]::new()
   $lockDir = [IO.Directory]::CreateDirectory((Join-Path ([IO.Path]::GetTempPath()) 'royder-headless/RemoteDevHost-ports')).FullName
   foreach ($port in (Get-ExperimentPorts $plan | Sort-Object)) {
      $portLocks += [IO.File]::Open((Join-Path $lockDir "$port.lock"), 'OpenOrCreate', 'ReadWrite', 'None')
   }
   Assert-PortsFree $plan
   $previousLock = Join-Path $plan.profile config/.lock
   if ($plan.Contains('managedAssets') -and [IO.File]::Exists($previousLock)) {
      $previousPid = 0
      if (![int]::TryParse([IO.File]::ReadAllText($previousLock).Trim(), [ref]$previousPid) -or
          (Get-Process -Id $previousPid -ErrorAction SilentlyContinue)) { throw "The private Rider config is locked. Inspect $previousLock before launching a JVM." }
   }
   foreach ($name in 'JAVA_TOOL_OPTIONS','JDK_JAVA_OPTIONS','_JAVA_OPTIONS') { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
   foreach ($setting in $plan.environment.GetEnumerator()) { [Environment]::SetEnvironmentVariable($setting.Key,$setting.Value) }
   $env:PATH = (Split-Path $plan.java) + ';' + $env:PATH
   if ($plan.Contains('managedAssets')) {
      $ownerFile = Join-Path $Run owner.json
      $owner = $env:ROYDER_MANAGED_OWNER | ConvertFrom-Json -AsHashtable
      if (!(Get-LiveIdentity $owner)) { throw 'The primary ended before its private Rider started.' }
      Write-HostJson $ownerFile $owner
      $orphaned = $null
      $token = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
      $encrypted = [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::ASCII.GetBytes($token), $null, 'CurrentUser')
      [IO.File]::WriteAllBytes((Join-Path $Run host-token.dpapi), $encrypted)
      $env:ROYDER_MANAGED_HOST_TOKEN = $token
   }
   $javaPid = $job.StartJava($plan.java, (Join-Path $Run host.args), $Run, (Join-Path $Run host))
   $javaProcess = [Diagnostics.Process]::GetProcessById($javaPid)
   $null = $javaProcess.Handle # Retain the handle so its exit code is available after the PID disappears.
   $state.hostProcess = Get-ProcessIdentity $javaProcess
   $state.phase = 'Running'
   Publish-State
   $deadline = $null
   $checkOwner = [Diagnostics.Stopwatch]::StartNew()
   $previousMembers = ''
   while ($true) {
      $ids = $job.ProcessIds()
      $members = @(foreach ($member in $ids) {
         $process = Get-Process -Id $member -ErrorAction SilentlyContinue
         if ($process) {
            $identity = Get-ProcessIdentity $process
            if (!$identity) { continue } # A short-lived worker can exit between enumeration and reading its start time.
            $identityKey = "$member/$($identity.started)"
            if (!$observed.ContainsKey($identityKey)) {
               $identity.parentPid = [Royder.Primary.Hosting.Launcher.WindowsJob]::ParentOf($member)
               $observed[$identityKey] = $identity
            } else { $identity = $observed[$identityKey] }
            $identity
         }
      })
      $state.members = $members
      $key = $ids -join ','
      $requested = $null
      if ($plan.Contains('managedAssets') -and [IO.File]::Exists((Join-Path $Run observe.json))) { $requested = Read-HostJson (Join-Path $Run observe.json) }
      if ($key -ne $previousMembers -or ($requested -and $requested -ne $state['observationRequest'])) {
         $state.observationRequest = $requested
         $state.observed = @($observed.Values)
         Publish-State
         $previousMembers = $key
      }
      if ($javaProcess.WaitForExit(0)) {
         $state.exitCode = $javaProcess.ExitCode
         if ($state.phase -ne 'Stopping') { throw "Java exited unexpectedly with code $($state.exitCode)." }
         break
      }
      if ([IO.File]::Exists((Join-Path $Run 'abandon.request'))) {
         $state.phase = 'Stopping'
         break  # Initial open failed before any caller received this private job.
      }
      if ($plan.Contains('managedAssets') -and $checkOwner.Elapsed.TotalSeconds -ge 2) {
         $checkOwner.Restart()
         if (Get-LiveIdentity (Read-HostJson $ownerFile)) { $orphaned = $null }
         elseif (!$orphaned) { $orphaned = [Diagnostics.Stopwatch]::StartNew() }
         elseif ($orphaned.Elapsed.TotalSeconds -ge 60) { Request-HostStop $Run }
      }
      if (!$deadline -and [IO.File]::Exists((Join-Path $Run stop.request))) {
         $state.phase = 'Stopping'
         Publish-State
         $lockPath = Join-Path $plan.profile config/.lock
         if ([IO.File]::Exists($lockPath) -and [IO.File]::ReadAllText($lockPath).Trim() -eq [string]$javaPid) {
            $null = $job.StartJava($plan.java, (Join-Path $Run exit.args), $Run, (Join-Path $Run exit))
         } else { break } # The JVM has not yet acquired its profile. Only the job can stop it safely.
         $deadline = [Diagnostics.Stopwatch]::StartNew()
      }
      if ($deadline -and $deadline.Elapsed.TotalSeconds -ge 25) { break }
      Start-Sleep -Milliseconds 200
   }
} catch {
   $state.error = $_.Exception.Message
} finally {
   try {
      if ($job) {
         # Let processes already exiting finish; a disconnected jetbrainsd normally waits several minutes.
         if ($state.phase -eq 'Stopping' -and $javaProcess -and $javaProcess.HasExited) {
            $until = [datetime]::UtcNow.AddSeconds(2)
            while ($job.ProcessIds().Count -and [datetime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 100 }
         }
         $state.terminated = @($job.ProcessIds())
         if ($state.terminated.Count) { $job.Terminate() }
         $until = [datetime]::UtcNow.AddSeconds(10)
         while ($job.ProcessIds().Count -and [datetime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 100 }
         $state.jobRemaining = @($job.ProcessIds())
      }
   } finally {
      if ($job) { $job.Dispose() }
      $state.survivors = @($observed.Values | Where-Object { Get-LiveIdentity $_ })
      $state.observed = @($observed.Values)
      $state.members = @()
      $reserved = @(Get-ExperimentPorts $plan)
      $state.listeningAfter = @(Get-ListeningPorts | Where-Object { $_ -in $reserved } | Sort-Object -Unique)
      $state.phase = if ($state.error) { 'Failed' } else { 'Stopped' }
      Publish-State
      foreach ($handle in $portLocks) { $handle.Dispose() }
      if ($activeProfile) { $activeProfile.Dispose() }
   }
}
