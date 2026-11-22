param([Parameter(Mandatory)][string] $Cache, [string] $JbHome, [switch] $IncludeZoneProbe, [switch] $LiveDaemon)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$artifacts = Join-Path $PSScriptRoot 'obj/package-build'
$hostName = 'DotNetAnalysisAndRefactoringMcp'
$toolsName = "$hostName.Tools"
dotnet build "$repo/src/$toolsName" --artifacts-path $artifacts -c Debug -v:q --nologo | Out-Host
if ($LASTEXITCODE) { throw 'The Royder build failed.' }
$hostFolder = "$artifacts/bin/$hostName/debug"
$hostHash = (Get-FileHash "$hostFolder/$hostName.dll").Hash.ToLowerInvariant()
$toolsFile = "$artifacts/bin/$toolsName/debug/$toolsName.dll"
$toolsHash = (Get-FileHash $toolsFile).Hash.ToLowerInvariant()
$packageHash = $hostHash
$packageFiles = @("$hostFolder/$hostName.dll")
$probeProjects = @()
if ($IncludeZoneProbe) { $probeProjects += "$PSScriptRoot/ZoneProbe/Royder.ZoneProbe.csproj" }
if ($LiveDaemon) { $probeProjects += "$PSScriptRoot/LiveDaemon/Royder.LiveDaemon.csproj" }
foreach ($project in $probeProjects) {
   dotnet build $project --artifacts-path $artifacts -c Debug "-p:JbHome=$JbHome" -v:q --nologo | Out-Host
   if ($LASTEXITCODE) { throw "Could not build $project." }
   $assemblyName = [IO.Path]::GetFileNameWithoutExtension($project)
   $assemblyFile = "$artifacts/bin/$assemblyName/debug/$assemblyName.dll"
   $hashInput = [Text.Encoding]::ASCII.GetBytes($packageHash + (Get-FileHash $assemblyFile).Hash)
   $packageHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($hashInput)).ToLowerInvariant()
   $packageFiles += $assemblyFile
}
$version = "1.0.0-host.$packageHash"
$feed = [IO.Directory]::CreateDirectory("$Cache/feed/$packageHash").FullName
if (!(Test-Path "$feed/Royder.LiveHost.$version.nupkg")) {
   $contents = [IO.Directory]::CreateDirectory("$Cache/package-contents/$packageHash").FullName
   Copy-Item -LiteralPath $packageFiles -Destination $contents
   dotnet pack "$PSScriptRoot/HeadlessHost.csproj" --no-build -c Debug -o $feed "-p:NuspecFile=$PSScriptRoot/Royder.nuspec" `
      "-p:NuspecBasePath=$contents" "-p:NuspecProperties=version=$version" -v:q --nologo | Out-Host
   if ($LASTEXITCODE) { throw 'Packing the local Royder extension failed.' }
}
$tools = [IO.Directory]::CreateDirectory("$Cache/tools/$hostHash/$toolsHash/live").FullName
if (!(Test-Path -LiteralPath "$tools/$toolsName.dll")) {
   Copy-Item -LiteralPath $toolsFile -Destination $tools
}
if (!(Test-Path -LiteralPath "$tools/host.sha256")) {
   [IO.File]::WriteAllText("$tools/host.sha256", $hostHash)
}
return @{ Feed = $feed; Version = $version; Tools = (Split-Path $tools) }
