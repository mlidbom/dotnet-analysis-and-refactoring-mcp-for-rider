param([Parameter(Mandatory)][string] $Cache, [string] $JbHome, [switch] $IncludeZoneProbe)
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
if ($IncludeZoneProbe) {
   dotnet build "$PSScriptRoot/ZoneProbe" --artifacts-path $artifacts -c Debug "-p:JbHome=$JbHome" -v:q --nologo | Out-Host
   if ($LASTEXITCODE) { throw 'The zone activator build failed.' }
   $zoneFile = "$artifacts/bin/Royder.ZoneProbe/debug/Royder.ZoneProbe.dll"
   $zoneHash = (Get-FileHash $zoneFile).Hash
   $packageHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes($hostHash + $zoneHash))).ToLowerInvariant()
   $packageFiles += $zoneFile
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
