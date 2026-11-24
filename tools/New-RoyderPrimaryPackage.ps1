#requires -Version 7.4
<#
.SYNOPSIS
   Builds a primary package from the checked-out commit, captures its tool catalog, and with -Install installs it once
   the everyday primary has no managed project open.
.DESCRIPTION
   The steps of dev_docs/headless-royder/standalone-primary.md's update, as one command. The package goes to
   <PackagesFolder>/<commit>. The catalog is captured by a private Rider of its own, on ports other than the running
   primary's, with a temporary settings file and profile that are deleted afterwards. The settings name no desktop
   port and no tools folder, so the capture checks the package's own tools build.
   A package of the commit with its catalog, built by an earlier run, such as to verify it before installing it, is used
   as it is.
   Update-InstalledRoyderPrimary.ps1 refuses while the primary has a managed project open; -Install retries it until
   they have closed, which they do after 30 idle minutes.
#>
[CmdletBinding()]
param([string]$PackagesFolder = (Join-Path $env:LOCALAPPDATA 'DotNetAnalysisAndRefactoringMcp/primary'),
      # The solution the capture opens: one in a repository the installed settings name.
      [string]$Solution = (Join-Path $PSScriptRoot '../ReSharperMcp.sln'),
      [switch]$Install,
      [int]$MinutesToWaitForTheManagedProjectsToClose = 60)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (git -C $root status --porcelain --untracked-files=no) { throw 'Commit first: the package is named after the commit it is built from.' }
$commit = git -C $root rev-parse --short HEAD
$package = Join-Path $PackagesFolder $commit

function New-Package {
   & "$PSScriptRoot/Build-HeadlessRoyder.ps1" -OutputDirectory $package

   $installed = Get-Content -Raw -LiteralPath (Join-Path $PackagesFolder 'settings.json') | ConvertFrom-Json
   $manifest = Get-Content -Raw -LiteralPath (Join-Path $package 'RiderHost/package.json') | ConvertFrom-Json
   $captureProfile = Join-Path $PackagesFolder "capture-profile-$commit"
   $captureSettings = Join-Path $PackagesFolder "capture-settings-$commit.json"
   [ordered]@{
      port = 24930
      repositories = $installed.repositories
      catalogFile = Join-Path $package 'catalog.json'
      rider = [ordered]@{
         profileDirectory = $captureProfile
         riderHome = $installed.rider.riderHome
         assetsDirectory = Join-Path $package 'RiderHost'
         toolsBuild = $manifest.build
         remoteDevPort = 24931
         backendMcpPort = 24941
      }
   } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $captureSettings -Encoding utf8NoBOM
   try {
      & (Join-Path $package 'Royder.Primary.exe') prepare-catalog $captureSettings (Resolve-Path $Solution).Path
      if ($LASTEXITCODE) { throw "The catalog capture failed with exit code $LASTEXITCODE." }
   } finally {
      Remove-Item -LiteralPath $captureSettings -Force
      foreach ($attempt in 1..10) {
         # The capture's Rider can hold files of its profile a moment after it exits.
         try { if (Test-Path -LiteralPath $captureProfile) { Remove-Item -LiteralPath $captureProfile -Recurse -Force }; break }
         catch { if ($attempt -eq 10) { Write-Warning "Couldn't delete $($captureProfile): $_" } else { Start-Sleep -Seconds 3 } }
      }
   }
   Write-Host "Packaged $commit in $package"
}

if (Test-Path -LiteralPath (Join-Path $package 'catalog.json')) { Write-Host "Using $package, packaged before." }
elseif (Test-Path -LiteralPath $package) { throw "$package exists without a catalog: a run that didn't finish. Delete it first." }
else { New-Package }

if ($Install) {
   $until = (Get-Date).AddMinutes($MinutesToWaitForTheManagedProjectsToClose)
   while ($true) {
      try { & "$PSScriptRoot/Update-InstalledRoyderPrimary.ps1" -PackageDirectory $package; break }
      catch {
         if ($_.Exception.Message -notmatch 'managed projects open|has not drained|HttpClient.Timeout' -or (Get-Date) -gt $until) { throw }
         Start-Sleep -Seconds 30
      }
   }
   Write-Host "Installed $commit"
}
