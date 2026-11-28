#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
      [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-][A-Za-z0-9_.-]*$')][string]$ToolsBuild,
      [string]$RiderHome = (Join-Path $env:LOCALAPPDATA 'Programs/Rider'),
      [string]$LifecyclePlugin,
      [string]$RoyderPlugin,
      [string]$ToolsDirectory)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../../..").Path
$launcher = Join-Path $PSScriptRoot Launcher
. "$launcher/Common.ps1"
. "$launcher/HostProfile.ps1"
$product = Read-HostJson (Join-Path $RiderHome product-info.json)
$name = 'DotNetAnalysisAndRefactoringMcp'
if (!$RoyderPlugin) { $RoyderPlugin = Join-Path $env:APPDATA "JetBrains/$($product.dataDirectoryName)/plugins/$name" }
if (!$ToolsDirectory) { $ToolsDirectory = Join-Path $env:LOCALAPPDATA "$name/tools/$ToolsBuild" }
if (!$LifecyclePlugin) {
   & "$repo/experiments/RemoteCloseWorktree/Build.ps1" -RiderHome $RiderHome
   $LifecyclePlugin = "$repo/experiments/RemoteCloseWorktree/bin/plugin/RemoteCloseWorktree"
}
& dotnet build "$repo/experiments/RemoteRoslynPatcher/RemoteRoslynPatcher.csproj" -c Release --disable-build-servers "-p:RiderHome=$RiderHome" --nologo -v:q
if ($LASTEXITCODE) { throw 'Roslyn patcher build failed.' }

$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) { throw "Choose a fresh assets directory so an active generation can keep its current package: $destination" }
$parent = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)).FullName
$stageName = '.royder-package-' + [guid]::NewGuid().ToString('N')
$stage = [IO.Directory]::CreateDirectory((Join-Path $parent $stageName)).FullName
try {
   $null = [IO.Directory]::CreateDirectory((Join-Path $stage launcher))
   foreach ($file in 'RemoteDevHost.ps1','Common.ps1','HostProfile.ps1','ProfileAudit.ps1','Supervise.ps1','ManagedRider.ps1','WindowsJob.cs','ConnectedRiderRequest.cs','FileReplacement.cs') {
      Copy-Item -LiteralPath (Join-Path $launcher $file) -Destination (Join-Path $stage launcher)
   }
   Copy-PluginTree $RoyderPlugin (Join-Path $stage "plugins/$name")
   Copy-PluginTree $LifecyclePlugin (Join-Path $stage 'plugins/RemoteCloseWorktree')
   Copy-PluginTree "$repo/experiments/RemoteRoslynPatcher/bin/Release/net10.0/plugin/RemoteRoslynPatcher" (Join-Path $stage 'plugins/RemoteRoslynPatcher')
   Copy-PluginTree $ToolsDirectory (Join-Path $stage "tools/$ToolsBuild")
   $lifecycleJar = Join-Path $stage 'plugins/RemoteCloseWorktree/lib/RemoteCloseWorktree.jar'
   $archive = [IO.Compression.ZipFile]::OpenRead($lifecycleJar)
   try {
      foreach ($handler in 'OpenWorktreeService','CloseWorktreeService','PrivateProjectStore') {
         if (!$archive.GetEntry("dev/magnus/royder/remotecloseworktree/$handler.class")) { throw "Lifecycle plugin lacks $handler." }
      }
   } finally { $archive.Dispose() }
   $hostHash = (Get-FileHash -LiteralPath (Join-Path $stage "plugins/$name/dotnet/$name.dll")).Hash.ToLowerInvariant()
   if ([IO.File]::ReadAllText((Join-Path $stage "tools/$ToolsBuild/host.sha256")).Trim() -ne $hostHash) { throw 'Selected Royder host and tools build are incompatible.' }
   $toolsHash = (Get-FileHash -LiteralPath (Join-Path $stage "tools/$ToolsBuild/$name.Tools.dll")).Hash.ToLowerInvariant()
   $manifest = @{ build=$ToolsBuild; riderBuild=$product.buildNumber; hostHash=$hostHash; toolsHash=$toolsHash; privateProjectStores=$true
                  lifecycleHash=(Get-FileHash -LiteralPath $lifecycleJar).Hash
                  patcherHash=(Get-FileHash -LiteralPath (Join-Path $stage 'plugins/RemoteRoslynPatcher/dotnet/Royder.RemoteRoslynPatcher.dll')).Hash }
   Write-HostJson (Join-Path $stage package.json) $manifest
   [IO.Directory]::Move($stage,$destination)
   Write-Output "Staged Rider $($product.buildNumber), tools $ToolsBuild and both management plugins in $destination"
} finally {
   if ((Test-Path -LiteralPath $stage) -and (Split-Path -Parent $stage) -eq $parent -and (Split-Path -Leaf $stage) -eq $stageName) {
      Remove-Item -LiteralPath $stage -Recurse -Force
   }
}
