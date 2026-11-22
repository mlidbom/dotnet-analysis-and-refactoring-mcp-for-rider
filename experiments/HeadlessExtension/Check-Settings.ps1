#requires -Version 7
param([ValidateSet('default','two-keys','three-keys','personal','personal-dsl','profile-conflict','injected-conflict')]
      [string[]]$Cases = @('default','two-keys','three-keys','personal','personal-dsl','profile-conflict','injected-conflict'))
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path "$PSScriptRoot/../..").Path
$root = Join-Path ([IO.Path]::GetTempPath()) "royder-headless/$(Split-Path $repository -Leaf)"
$id = [guid]::NewGuid().ToString('N')
$experiment = [IO.Directory]::CreateDirectory("$root/settings-$id").FullName
$fixture = [IO.Directory]::CreateDirectory("$experiment/source").FullName
$projectFolder = Join-Path $repository "tools/DotNetAnalysisAndRefactoringMcp.DevelopmentTools/SettingsProbe$id"
Write-Host "Settings evidence: $experiment"
python -B "$PSScriptRoot/settings_probe.py" prepare $experiment $projectFolder
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the settings probes.' }
$created = $null
try {
   if ($Cases -contains 'default') {
      $created = New-Item -ItemType Directory -Path $projectFolder
      $editorConfig = Join-Path $fixture '.editorconfig'
      [void](New-Item -ItemType SymbolicLink -Path (Join-Path $projectFolder '.editorconfig') -Target $editorConfig)
      foreach ($file in 'Control.cs','Raised.cs') {
         Copy-Item -LiteralPath (Join-Path $fixture $file) -Destination $projectFolder
      }
   }
   foreach ($case in $Cases) {
      Write-Host "Comparing settings for $case"
      & "$PSScriptRoot/Run.ps1" -SettingsExperiment $experiment -SettingsCase $case
   }
} finally {
   if ($created) {
      foreach ($file in 'Control.cs','Raised.cs') {
         $path = Join-Path $created.FullName $file
         if (Test-Path -LiteralPath $path) {
            if ((Get-FileHash -LiteralPath $path).Hash -ne (Get-FileHash -LiteralPath (Join-Path $fixture $file)).Hash) {
               throw "Probe file changed during the run: $path"
            }
            [IO.File]::Delete($path)
         }
      }
      $link = Get-Item -LiteralPath (Join-Path $created.FullName '.editorconfig') -ErrorAction SilentlyContinue
      if ($link) {
         if ($link.LinkType -ne 'SymbolicLink' -or $link.LinkTarget -ne $editorConfig) { throw "Unexpected file at $($link.FullName)." }
         [IO.File]::Delete($link.FullName)
      }
      [IO.Directory]::Delete($created.FullName)
   }
   python -B "$PSScriptRoot/settings_probe.py" verify $experiment
   $hashCheckExitCode = $LASTEXITCODE
}
if ($hashCheckExitCode -ne 0) { throw "Settings file hashes differ. See $experiment." }
