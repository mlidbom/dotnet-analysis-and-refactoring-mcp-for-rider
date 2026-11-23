#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
      [string]$RiderHome = (Join-Path $env:LOCALAPPDATA 'Programs/Rider'))
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/..").Path
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $destination) { throw "Choose a fresh package directory: $destination" }
$parent = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)).FullName
$stagingName = '.royder-build-' + [guid]::NewGuid().ToString('N')
$stage = [IO.Directory]::CreateDirectory((Join-Path $parent $stagingName)).FullName
try {
   $name = 'DotNetAnalysisAndRefactoringMcp'
   $product = Get-Content -Raw -LiteralPath (Join-Path $RiderHome product-info.json) | ConvertFrom-Json
   $installed = Join-Path $env:APPDATA "JetBrains/$($product.dataDirectoryName)/plugins/$name"
   $plugin = [IO.Directory]::CreateDirectory((Join-Path $stage "input/$name")).FullName
   $backend = [IO.Directory]::CreateDirectory((Join-Path $plugin dotnet)).FullName
   Copy-Item -LiteralPath (Join-Path $installed lib) -Destination (Join-Path $plugin lib) -Recurse
   & dotnet build "$root/src/$name.Tools/$name.Tools.csproj" -c Release --nologo -v:q
   if($LASTEXITCODE) { throw 'Building the Royder tools failed.' }
   $hostAssembly = "$root/src/$name/bin/Release/net472/$name.dll"
   $toolsAssembly = "$root/src/$name.Tools/bin/Release/net472/$name.Tools.dll"
   Copy-Item -LiteralPath $hostAssembly -Destination $backend
   $tools = [IO.Directory]::CreateDirectory((Join-Path $stage 'input/tools')).FullName
   Copy-Item -LiteralPath $toolsAssembly,([IO.Path]::ChangeExtension($toolsAssembly,'pdb')) -Destination $tools
   $hash = (Get-FileHash -LiteralPath $hostAssembly).Hash.ToLowerInvariant()
   [IO.File]::WriteAllText((Join-Path $tools host.sha256), $hash)
   $build = 'primary-' + (Get-FileHash -LiteralPath $toolsAssembly).Hash.Substring(0,16).ToLowerInvariant()
   $output = Join-Path $stage package
   & dotnet publish "$root/src/Royder.Primary/Royder.Primary.csproj" -c Release -o $output --nologo -v:q
   if($LASTEXITCODE) { throw 'Building the standalone primary failed.' }
   & "$root/src/Royder.Primary/Hosting/Stage-SharedRider.ps1" -RiderHome $RiderHome -OutputDirectory (Join-Path $output RiderHost) -ToolsBuild $build -RoyderPlugin $plugin -ToolsDirectory $tools
   # A directory with a file open in it can't be moved, and a file just written is held for a moment, as by a virus
   # scanner: on 2026-11-24 the move failed with access denied twice in a row, the deletion just after it succeeding.
   for($attempt = 1; ; $attempt++) {
      try { [IO.Directory]::Move($output,$destination); break }
      catch [UnauthorizedAccessException], [IO.IOException] { if($attempt -ge 40) { throw }; Start-Sleep -Milliseconds 500 }
   }
   if($attempt -gt 1) { Write-Output "Moving the package into place took $attempt attempts." }
   Write-Output "Built $destination (tools $build)."
} finally {
   if ((Split-Path -Parent $stage) -eq $parent -and (Split-Path -Leaf $stage) -eq $stagingName) {
      Remove-Item -LiteralPath $stage -Recurse -Force
   }
}
