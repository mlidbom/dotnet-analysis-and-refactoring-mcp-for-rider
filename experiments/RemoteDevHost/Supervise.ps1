param([Parameter(Mandatory)][string]$Run)
. "$PSScriptRoot/Common.ps1"

$plan = Read-HostJson (Join-Path $Run plan.json)
$observed = @{}
$portLocks = @()
$job = $null
$activeProfile = $null
$state = @{ phase='Starting'; supervisor=Get-ProcessIdentity (Get-Process -Id $PID); hostProcess=$null; members=@()
            minAvailableBytes=[long]::MaxValue; error=$null; terminated=@(); survivors=@(); jobRemaining=@(); listeningAfter=@(); exitCode=$null }
function Publish-State { Write-HostJson (Join-Path $Run state.json) $state }

try {
   Publish-State
   $activeProfile = [IO.File]::Open((Join-Path $plan.profile host.lock), 'OpenOrCreate','ReadWrite','None')
   Add-Type -Path "$PSScriptRoot/WindowsJob.cs"
   $job = [RemoteDevHost.WindowsJob]::new()
   $lockDir = [IO.Directory]::CreateDirectory((Join-Path ([IO.Path]::GetTempPath()) 'royder-headless/RemoteDevHost-ports')).FullName
   foreach ($port in (Get-ExperimentPorts $plan | Sort-Object)) {
      $portLocks += [IO.File]::Open((Join-Path $lockDir "$port.lock"), 'OpenOrCreate', 'ReadWrite', 'None')
   }
   Assert-PortsFree $plan
   foreach ($name in 'JAVA_TOOL_OPTIONS','JDK_JAVA_OPTIONS','_JAVA_OPTIONS') { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
   foreach ($setting in $plan.environment.GetEnumerator()) { [Environment]::SetEnvironmentVariable($setting.Key,$setting.Value) }
   $env:PATH = (Split-Path $plan.java) + ';' + $env:PATH
   $state.startAvailableBytes = [RemoteDevHost.WindowsJob]::AvailablePhysicalBytes
   if ($state.startAvailableBytes -lt 8GB) { throw "Below 8 GiB before Java start ($($state.startAvailableBytes) bytes available)." }
   $javaPid = $job.StartJava($plan.java, (Join-Path $Run host.args), $Run, (Join-Path $Run host))
   $javaProcess = [Diagnostics.Process]::GetProcessById($javaPid)
   $null = $javaProcess.Handle # Retain the handle so its exit code is available after the PID disappears.
   $state.hostProcess = Get-ProcessIdentity $javaProcess
   $state.phase = 'Running'
   Publish-State
   $deadline = $null
   $previousMembers = ''
   while ($true) {
      $available = [RemoteDevHost.WindowsJob]::AvailablePhysicalBytes
      $state.minAvailableBytes = [Math]::Min($state.minAvailableBytes, $available)
      if ($available -lt 2GB) { throw "Stopped the job below 2 GiB ($available bytes available)." }
      $ids = $job.ProcessIds()
      $members = @(foreach ($member in $ids) {
         $process = Get-Process -Id $member -ErrorAction SilentlyContinue
         if ($process) {
            $identity = Get-ProcessIdentity $process
            if (!$identity) { continue } # A short-lived worker can exit between enumeration and reading its start time.
            $observed["$member/$($identity.started)"] = $identity
            $identity
         }
      })
      $state.members = $members
      $key = $ids -join ','
      if ($key -ne $previousMembers) { Publish-State; $previousMembers = $key }
      if ($javaProcess.WaitForExit(0)) {
         $state.exitCode = $javaProcess.ExitCode
         if ($state.phase -ne 'Stopping') { throw "Java exited unexpectedly with code $($state.exitCode)." }
         break
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
            while ($job.ProcessIds().Count -and [datetime]::UtcNow -lt $until -and [RemoteDevHost.WindowsJob]::AvailablePhysicalBytes -ge 2GB) { Start-Sleep -Milliseconds 100 }
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
