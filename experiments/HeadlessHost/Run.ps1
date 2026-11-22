param(
   [string] $Solution = (Join-Path $PSScriptRoot '..\..\ReSharperMcp.sln'),
   [string] $Symbol = 'ToolBatch.Of',
   [string] $Cache,
   [switch] $Listen,
   [switch] $Royder,
   [switch] $LiveDaemon,
   [switch] $NoSolutionWideAnalysis,
   [ValidateSet('editing', 'external-sources', 'both')][string] $RequestZones,
   [switch] $ProbeMissingImport,
   [string] $JbHome = (Join-Path $env:USERPROFILE '.dotnet\tools\.store\jetbrains.resharper.globaltools\2026.3.0.1\jetbrains.resharper.globaltools\2026.3.0.1\tools\net10.0\any'),
   [ValidateSet(23910,23920,23921)][int] $ExpectedRoyderPort = 23910
)
$ErrorActionPreference = 'Stop'
if ($RequestZones -and !$Royder) { throw '-RequestZones requires -Royder.' }
if ($LiveDaemon -and !$Royder) { throw '-LiveDaemon requires -Royder.' }
function Get-AvailableMemoryBytes {
   return [long](Get-CimInstance Win32_OperatingSystem -Property FreePhysicalMemory).FreePhysicalMemory * 1KB
}
function Assert-EnoughMemoryToStart {
   $available = Get-AvailableMemoryBytes
   if ($available -lt 4GB) { throw "Headless engine needs 4 GiB available to start. Available bytes: $available" }
   return $available
}
$null = Assert-EnoughMemoryToStart
if (!$Cache) {
   $Cache = Join-Path $PSScriptRoot $(if ($Royder) { 'obj/live' } else { 'obj/state' })
   if ($RequestZones) { $Cache = Join-Path $Cache "zones-$RequestZones" }
   if ($LiveDaemon) { $Cache = Join-Path $Cache 'live-daemon' }
}
dotnet build $PSScriptRoot --nologo -v:q "-p:JbHome=$JbHome"
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$Cache = [IO.Path]::GetFullPath($Cache)
$engine = [Diagnostics.ProcessStartInfo]::new('dotnet')
$engine.UseShellExecute = $false
if ($ProbeMissingImport) {
   $existingDefines = $engine.Environment['DefineConstants']
   $engine.Environment['DefineConstants'] = "$existingDefines;ROYDER_HEADLESS_IMPORT_PROBE".TrimStart(';')
}
$engine.Environment['DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT'] = if ($ExpectedRoyderPort -eq 23921) { '23920' } else { [string]$ExpectedRoyderPort }
$engine.Environment['JETBRAINS_NO_CHECK_FOR_UPDATES'] = '1'
$engine.Environment['TEMP'] = [IO.Directory]::CreateDirectory((Join-Path $Cache 'temp')).FullName
$engine.Environment['TMP'] = $engine.Environment['TEMP']
$engine.Environment['JET_TEMP_DIR'] = $engine.Environment['TEMP']
$engine.Environment['JET_LOCAL_APPDATA_DIR'] = [IO.Directory]::CreateDirectory((Join-Path $Cache 'local')).FullName
$engine.Environment['JET_ROAMING_APPDATA_DIR'] = [IO.Directory]::CreateDirectory((Join-Path $Cache 'roaming')).FullName
$engine.Environment['RESHARPER_HOST_LOG_DIR'] = [IO.Directory]::CreateDirectory((Join-Path $Cache 'logs')).FullName
foreach ($argument in 'exec', '--depsfile', "$JbHome\inspectcode.deps.json", "$PSScriptRoot\bin\Debug\net10.0-windows\HeadlessHost.dll",
         (Resolve-Path -LiteralPath $Solution).Path, (Join-Path $Cache 'solution-caches'), $Symbol) {
   $engine.ArgumentList.Add($argument)
}
if ($Listen) { $engine.ArgumentList.Add('--listen') }
if ($NoSolutionWideAnalysis) { $engine.ArgumentList.Add('--no-swea') }
if ($Royder) {
   $package = & "$PSScriptRoot/PrepareRoyder.ps1" -Cache $Cache -JbHome $JbHome -IncludeZoneProbe:([bool]$RequestZones) -LiveDaemon:$LiveDaemon
   if ($RequestZones) { $engine.Environment['ROYDER_ZONE_REQUEST'] = $RequestZones }
   $engine.Environment['DOTNET_ANALYSIS_AND_REFACTORING_MCP_TOOLS'] = $package.Tools
   $engine.ArgumentList.Add("-x=Royder.LiveHost/[$($package.Version)]")
   $engine.ArgumentList.Add("--source=$($package.Feed)")
   $engine.ArgumentList.Add("--LogFolder=$Cache/logs")
   $listeners = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port
   if ($listeners -contains $ExpectedRoyderPort) { throw "Port $ExpectedRoyderPort is already in use." }
   if ($ExpectedRoyderPort -eq 23921 -and $listeners -notcontains 23920) { throw 'The routing primary must be running first.' }
   if ($ExpectedRoyderPort -ne 23910) {
      $available = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory * 1KB
      if ($available -lt 6GB) { throw "Routing host requires 6 GiB available; measured $available bytes before engine start." }
      Write-Host "Routing engine start on $ExpectedRoyderPort with $available bytes available."
   }
}
$startAvailable = Assert-EnoughMemoryToStart
$minimumAvailable = $startAvailable
$process = [Diagnostics.Process]::Start($engine)
try {
   while (!$process.WaitForExit(1000)) {
      $available = Get-AvailableMemoryBytes
      $minimumAvailable = [Math]::Min($available, $minimumAvailable)
      if ($available -lt 2GB) { throw "Stopping headless PID $($process.Id) below 2 GiB available. Available bytes: $available" }
   }
   exit $process.ExitCode
} finally {
   if (!$process.HasExited) {
      $process.Kill($true)
      $process.WaitForExit()
   }
   Write-Host "MEMORY startAvailableBytes=$startAvailable minimumSampledAvailableBytes=$minimumAvailable"
   $process.Dispose()
}
