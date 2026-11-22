param([Parameter(Mandatory)][string] $Cache)
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
$version = "1.0.0-host.$hostHash"
$feed = [IO.Directory]::CreateDirectory("$Cache/feed/$hostHash").FullName
if (!(Test-Path "$feed/Royder.LiveHost.$version.nupkg")) {
   dotnet pack "$PSScriptRoot/HeadlessHost.csproj" --no-build -c Debug -o $feed "-p:NuspecFile=$PSScriptRoot/Royder.nuspec" `
      "-p:NuspecBasePath=$hostFolder" "-p:NuspecProperties=version=$version" -v:q --nologo | Out-Host
   if ($LASTEXITCODE) { throw 'Packing the local Royder extension failed.' }
}
$tools = [IO.Directory]::CreateDirectory("$Cache/tools/$hostHash/$toolsHash/live").FullName
Copy-Item -LiteralPath $toolsFile -Destination $tools
[IO.File]::WriteAllText("$tools/host.sha256", $hostHash)
return @{ Feed = $feed; Version = $version; Tools = (Split-Path $tools) }
