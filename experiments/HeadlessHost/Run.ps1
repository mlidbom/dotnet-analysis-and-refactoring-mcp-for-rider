param(
   [string] $Solution = (Join-Path $PSScriptRoot '..\..\ReSharperMcp.sln'),
   [string] $Symbol = 'ToolBatch.Of',
   [string] $Cache,
   [switch] $Listen,
   [switch] $Royder,
   [ValidateSet('editing', 'external-sources', 'both')][string] $RequestZones,
   [switch] $ProbeMissingImport,
   [string] $JbHome = (Join-Path $env:USERPROFILE '.dotnet\tools\.store\jetbrains.resharper.globaltools\2026.3.0.1\jetbrains.resharper.globaltools\2026.3.0.1\tools\net10.0\any'),
   [ValidateSet(23910,23920,23921)][int] $ExpectedRoyderPort = 23910
)
$ErrorActionPreference = 'Stop'
if ($RequestZones -and !$Royder) { throw '-RequestZones requires -Royder.' }
if (!$Cache) {
   $Cache = Join-Path $PSScriptRoot $(if ($Royder) { 'obj/live' } else { 'obj/state' })
   if ($RequestZones) { $Cache = Join-Path $Cache "zones-$RequestZones" }
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
if ($Royder) {
   $package = & "$PSScriptRoot/PrepareRoyder.ps1" -Cache $Cache -JbHome $JbHome -IncludeZoneProbe:([bool]$RequestZones)
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
$process = [Diagnostics.Process]::Start($engine)
try {
   $process.WaitForExit()
   exit $process.ExitCode
} finally {
   $process.Dispose()
}
