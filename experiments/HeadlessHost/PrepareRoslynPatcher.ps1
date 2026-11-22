param([Parameter(Mandatory)][string] $Feed, [Parameter(Mandatory)][string] $JbHome)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path "$PSScriptRoot/../HeadlessRoslynPatcher/RoslynPatcher/Royder.Headless.RoslynPatcher.csproj").Path
$name = [IO.Path]::GetFileNameWithoutExtension($project)
dotnet build $project -c Debug "-p:JbHome=$JbHome" -v:q --nologo | Out-Host
if ($LASTEXITCODE) { throw 'The Roslyn working-directory patcher build failed.' }
$assembly = Join-Path (Split-Path $project) "bin/Debug/net10.0/$name.dll"
$hash = (Get-FileHash -LiteralPath $assembly).Hash.ToLowerInvariant()
$version = "1.0.0-worker.$hash"
if (!(Test-Path -LiteralPath "$Feed/$name.$version.nupkg")) {
   dotnet pack $project --no-build -c Debug "-p:JbHome=$JbHome" "-p:PackageVersion=$version" -o $Feed -v:q --nologo | Out-Host
   if ($LASTEXITCODE) { throw 'Packing the Roslyn working-directory patcher failed.' }
}
return "$name/[$version]"
