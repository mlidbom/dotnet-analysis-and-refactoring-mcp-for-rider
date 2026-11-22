#requires -Version 7.4
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-HostJson($Path, $Value) {
   $pending = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
   [IO.File]::WriteAllText($pending, (ConvertTo-Json -InputObject $Value -Depth 12))
   try {
      # A Windows reader without FILE_SHARE_DELETE briefly prevents replacing its open snapshot.
      $until = [datetime]::UtcNow.AddSeconds(2)
      while ($true) {
         try { [IO.File]::Move($pending, $Path, $true); break }
         catch {
            $cause = $_.Exception.GetBaseException()
            if (($cause.HResult -band 0xffff) -notin 5,32,33 -or [datetime]::UtcNow -ge $until) { throw }
            Start-Sleep -Milliseconds 30
         }
      }
   } finally { [IO.File]::Delete($pending) }
}

function Read-HostJson($Path) {
   # Allow the supervisor to replace the file while a caller reads its previous snapshot.
   $stream = [IO.FileStream]::new($Path, 'Open', 'Read', [IO.FileShare]'ReadWrite,Delete')
   try {
      $reader = [IO.StreamReader]::new($stream)
      try { $reader.ReadToEnd() | ConvertFrom-Json -AsHashtable } finally { $reader.Dispose() }
   } finally { $stream.Dispose() }
}

function Get-ProfilePath([string]$Path, [switch]$Managed) {
   $tempRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))
   $full = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Path))
   if (!$Managed -and !$full.StartsWith($tempRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
      throw "Choose a profile below $tempRoot."
   }
   if ($Managed) {
      if ($full -eq [IO.Path]::GetPathRoot($full)) { throw 'Choose a dedicated managed profile directory.' }
      foreach ($real in @($env:APPDATA, $env:LOCALAPPDATA)) {
         $jetbrains = [IO.Path]::GetFullPath((Join-Path $real JetBrains))
         if ($full -eq $jetbrains -or $full.StartsWith($jetbrains + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'The managed profile must be outside the JetBrains user directories.' }
      }
   }
   for ($dir = [IO.DirectoryInfo]::new($full); $dir -and $dir.FullName -ne $tempRoot; $dir = $dir.Parent) {
      if ($dir.Exists -and ($dir.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
         throw "Profile path crosses a link: $($dir.FullName)"
      }
   }
   return $full
}

function Get-ListeningPorts {
   [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port
}

function Get-LiveIdentity($Identity) {
   $process = Get-Process -Id $Identity.pid -ErrorAction SilentlyContinue
   if ($process) {
      $started = $process.StartTime
      if ($started -and $started.ToUniversalTime().Ticks -eq $Identity.started) { $process }
   }
}

function Get-ProcessIdentity([Diagnostics.Process]$Process) {
   $started = $Process.StartTime
   if ($started) { @{ pid = $Process.Id; started = $started.ToUniversalTime().Ticks; name = $Process.ProcessName } }
}

function Get-ExperimentPorts($Plan) {
   @([int]$Plan.remoteDevPort) + @($Plan.royderPort..($Plan.royderPort + 9))
}

function Assert-PortsFree($Plan) {
   $reserved = @(Get-ExperimentPorts $Plan)
   $taken = @(Get-ListeningPorts | Where-Object { $_ -in $reserved } | Sort-Object -Unique)
   if ($taken.Count) { throw "Requested ports already listening: $($taken -join ', ')" }
}

function Get-RunState([string]$Run) {
   $path = Join-Path $Run state.json
   if ([IO.File]::Exists($path)) { Read-HostJson $path }
}

function Request-HostStop([string]$Run) {
   [IO.File]::WriteAllText((Join-Path $Run stop.request), '')
}

function Get-AdvertisedPort([string]$Run, [switch]$IfStarted) {
   $cache = Join-Path $Run builtin-port.json
   if ([IO.File]::Exists($cache)) { return [int](Read-HostJson $cache) }
   foreach ($log in (Get-ChildItem -LiteralPath (Join-Path $Run log) -File -Filter 'idea*.log' | Sort-Object LastWriteTimeUtc -Descending)) {
      $announcement = Select-String -LiteralPath $log.FullName -Pattern '^\d{4}-\d{2}-\d{2} .* INFO - #o\.j\.i\.BuiltInServerManager - built-in server started, port (\d+)\s*$' | Select-Object -Last 1
      if ($announcement) {
         $port = [int]$announcement.Matches[0].Groups[1].Value
         Write-HostJson $cache $port
         return $port
      }
   }
   if (!$IfStarted) { throw "The private JVM has not announced its IntelliJ built-in HTTP port in $Run/log." }
}
