param(
   [string] $Solution = (Join-Path $PSScriptRoot '..\..\ReSharperMcp.sln'),
   [string] $Symbol = 'ToolBatch.Of',
   [string] $Cache = (Join-Path $PSScriptRoot 'obj\state'),
   [switch] $Listen,
   [switch] $Royder,
   [string] $JbHome = (Join-Path $env:USERPROFILE '.dotnet\tools\.store\jetbrains.resharper.globaltools\2026.3.0.1\jetbrains.resharper.globaltools\2026.3.0.1\tools\net10.0\any')
)
$ErrorActionPreference = 'Stop'
dotnet build $PSScriptRoot --nologo -v:q "-p:JbHome=$JbHome"
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$Cache = [IO.Path]::GetFullPath($Cache)
$engine = [Diagnostics.ProcessStartInfo]::new('dotnet')
$engine.UseShellExecute = $false
$engine.Environment['DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT'] = '23910'
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
   if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains 23910) {
      throw 'Port 23910 is already in use.'
   }
   $package = & "$PSScriptRoot/PrepareRoyder.ps1" -Cache $Cache
   $engine.Environment['DOTNET_ANALYSIS_AND_REFACTORING_MCP_TOOLS'] = $package.Tools
   $engine.ArgumentList.Add("-x=Royder.LiveHost/[$($package.Version)]")
   $engine.ArgumentList.Add("--source=$($package.Feed)")
   $engine.ArgumentList.Add("--LogFolder=$Cache/logs")
}
$process = [Diagnostics.Process]::Start($engine)
try {
   $process.WaitForExit()
   exit $process.ExitCode
} finally {
   $process.Dispose()
}
