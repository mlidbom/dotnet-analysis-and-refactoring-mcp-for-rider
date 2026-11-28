#requires -Version 7.4
[CmdletBinding()]
param(
   [Parameter(Mandatory)][ValidateSet('Stock','Private')][string]$Mode,
   [Parameter(Mandatory)][string]$ToolsBuild,
   [Parameter(Mandatory)][int]$RemoteDevPort,
   [Parameter(Mandatory)][int]$RoyderPort,
   [switch]$ExerciseHandoff,
   [string]$RiderHome = (Join-Path $env:LOCALAPPDATA 'Programs/Rider')
)
$harness = Join-Path $PSScriptRoot ../../src/Royder.Primary/Hosting/Launcher
. "$harness/Common.ps1"
if ($ExerciseHandoff -and $Mode -ne 'Private') { throw 'The owner-edit handoff check requires Private mode.' }
Add-Type -Path "$harness/ConnectedRiderRequest.cs"

function New-Checkout($Name) {
   $checkout = [IO.Directory]::CreateDirectory((Join-Path $fixture $Name)).FullName
   $settings = [IO.Directory]::CreateDirectory((Join-Path $checkout '.idea/.idea.Probe/.idea')).FullName
   $runs = [IO.Directory]::CreateDirectory((Join-Path $settings runConfigurations)).FullName
   [IO.File]::WriteAllText("$settings/workspace.xml", @"
<project version="4">
  <component name="ChangeListManager"><list default="true" id="b78e21e6-b1f4-4b78-af0c-7dbab4e6eea8" name="Personal $Name" comment="Unfinished work" /></component>
  <component name="PropertiesComponent"><![CDATA[{"keyToString":{"personal-project-store-fixture":"$Name"}}]]></component>
  <component name="RunManager" selected="Application.Personal $Name"/>
</project>
"@)
   [IO.File]::WriteAllText("$runs/Personal.xml", "<component name='ProjectRunConfigurationManager'><configuration default='false' name='Personal $Name' type='Application' factoryName='Application'/></component>")
   [IO.File]::WriteAllText("$settings/vcs.xml", '<project version="4"><component name="VcsDirectoryMappings"><mapping directory="$PROJECT_DIR$" vcs="Git"/></component></project>')
   [IO.File]::WriteAllText("$settings/.name", "Personal $Name")
   if ($Name -eq 'Alpha') {
      [IO.File]::WriteAllText("$checkout/Probe.sln", @'
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Probe", "Probe.csproj", "{548FF578-264F-4461-8365-D9BD138C01BB}"
EndProject
Global
   GlobalSection(SolutionConfigurationPlatforms) = preSolution
      Debug|Any CPU = Debug|Any CPU
   EndGlobalSection
   GlobalSection(ProjectConfigurationPlatforms) = postSolution
      {548FF578-264F-4461-8365-D9BD138C01BB}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
      {548FF578-264F-4461-8365-D9BD138C01BB}.Debug|Any CPU.Build.0 = Debug|Any CPU
   EndGlobalSection
EndGlobal
'@)
   } else {
      [IO.File]::WriteAllText("$checkout/Probe.slnx", '<Solution><Project Path="Probe.csproj"/></Solution>')
   }
   $solution = Get-Solution $checkout
   [IO.File]::WriteAllText("$solution.DotSettings.user", '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><!-- Private checkout fixture --></ResourceDictionary>')
   [IO.File]::WriteAllText("$checkout/Probe.csproj", '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
   [IO.File]::WriteAllText("$checkout/NuGet.Config", '<configuration><packageSources><clear/></packageSources></configuration>')
   [IO.File]::WriteAllText("$checkout/Calculator.cs", "public static class Calculator { public static int Add$Name(int left, int right) => left + right; }")
   [IO.File]::WriteAllText("$checkout/Consumer.cs", "public sealed class Consumer { public int Result => Calculator.Add$Name(1, 2); }")
   [IO.File]::WriteAllText("$checkout/.gitignore", "bin/`nobj/`n*.DotSettings.user`n.idea/**/workspace.xml`n")
   & git -C $checkout init --quiet
   if ($LASTEXITCODE) { throw 'Fixture git init failed' }
   & git -C $checkout add -A
   if ($LASTEXITCODE) { throw 'Fixture git add failed' }
   & git -C $checkout -c user.name=Fixture -c user.email=fixture@localhost commit --quiet -m 'Seed disposable main checkout'
   if ($LASTEXITCODE) { throw 'Fixture git commit failed' }
   & dotnet restore $solution --configfile "$checkout/NuGet.Config" -p:NuGetAudit=false --disable-build-servers *> "$fixture/restore-$Name.log"
   if ($LASTEXITCODE) { throw "Restore failed. See $fixture/restore-$Name.log" }
   $checkout
}

function Get-Solution($Checkout) {
   (Get-ChildItem -LiteralPath $Checkout -File | Where-Object Extension -in '.sln','.slnx').FullName
}

function Get-StoreSnapshot($Checkout) {
   $files = [ordered]@{}
   $settings = @(Get-ChildItem -File -Force -Recurse -LiteralPath "$Checkout/.idea") + @(Get-ChildItem -File -Force -LiteralPath $Checkout -Filter '*.DotSettings*')
   foreach ($file in $settings | Sort-Object FullName) {
      $files[[IO.Path]::GetRelativePath($Checkout, $file.FullName)] = [ordered]@{
         sha256=(Get-FileHash -LiteralPath $file.FullName).Hash; modified=$file.LastWriteTimeUtc.Ticks
      }
   }
   $files
}

function Record-Phase($Phase) {
   $snapshot = [ordered]@{}
   foreach ($checkout in $checkouts) {
      $snapshot[(Split-Path -Leaf $checkout)] = Get-StoreSnapshot $checkout
   }
   Write-HostJson "$fixture/$Phase.json" $snapshot
   [IO.File]::AppendAllText("$fixture/phases.txt", "$([datetime]::UtcNow.ToString('O')) $Phase`n")
}

function Send-Management($operation, $solution, $tag = '') {
   $client = [Net.Sockets.TcpClient]::new()
   try {
      $client.Connect([Net.IPAddress]::Loopback, $builtIn)
      $peer = @(Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort $builtIn -RemoteAddress 127.0.0.1 -RemotePort $client.Client.LocalEndPoint.Port -State Established)
      if ($peer.Count -ne 1 -or $peer[0].OwningProcess -ne $hostState.javaPid) { throw 'Private listener PID mismatch' }
      $route = "royder-$operation-worktree"
      if ($Mode -eq 'Private' -and $operation -eq 'open') { $route += '/private' }
      $url = [uri]"http://127.0.0.1:$builtIn/api/$route"
      $result = [Royder.Primary.Hosting.Launcher.ConnectedRiderRequest]::PostAsync($client, $url, $token, $solution, $tag, '').GetAwaiter().GetResult()
      if ($result.Status -ne 200) { throw "$operation $solution failed: $($result.Status) $($result.Message)" }
      $result.ETag
   } finally { $client.Dispose() }
}

function Call-Tool($Name, $Arguments) {
   $request = @{jsonrpc='2.0';id=1;method='tools/call';params=@{ name=$Name; arguments=$Arguments }}
   $response = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$RoyderPort/" -NoProxy -ContentType 'application/json' -Body ($request | ConvertTo-Json -Compress -Depth 8) -TimeoutSec 125
   if (!$response.result) { throw "MCP call $Name was rejected" }
   ($response.result.content.text -join "`n")
}

function Wait-ForFixtureReady($solution) {
   $deadline = [datetime]::UtcNow.AddMinutes(2)
   do {
      $ready = $false
      try {
         $health = Invoke-RestMethod "http://127.0.0.1:$RoyderPort/health" -NoProxy -TimeoutSec 3
         $ready = @($health.solutions | Where-Object { $_.path -eq $solution -and !$_.indexing }).Count -eq 1
      } catch [Net.Http.HttpRequestException] { } catch [OperationCanceledException] { }
      if ($ready) { break }
      if ([datetime]::UtcNow -gt $deadline) { throw "Private Royder did not register $solution" }
      Start-Sleep -Milliseconds 350
   } while ($true)
}
function Rename-InFixture($Checkout) {
   $name = Split-Path -Leaf $Checkout
   $solution = Get-Solution $Checkout
   $deadline = [datetime]::UtcNow.AddMinutes(2)
   do {
      $answer = Call-Tool find_usages @{solutionName=$solution; symbolName="Calculator.Add$name"}
      if ($answer.Contains("Consumer.cs") -and $answer.Contains("Calculator.Add$name(")) { break }
      if ([datetime]::UtcNow -gt $deadline) { throw "Real source reference did not resolve: $answer" }
      Start-Sleep -Milliseconds 500
   } while ($true)
   $rename = Call-Tool rename_symbol @{solutionName=$solution; symbolName="Calculator.Add$name"; newName="Sum$name"}
   [IO.File]::WriteAllText("$fixture/rename-$name.txt", $rename)
   foreach ($file in 'Calculator.cs','Consumer.cs') {
      if (!([IO.File]::ReadAllText("$Checkout/$file").Contains("Sum$name("))) { throw "Rename did not reach $Checkout/$file : $rename" }
   }
}

$fixture = [IO.Directory]::CreateDirectory((Join-Path $env:TEMP "royder-project-stores-$([guid]::NewGuid().ToString('N'))")).FullName
Write-Output "Evidence: $fixture"
$profileDir = Join-Path $fixture profile
$env:DOTNET_CLI_HOME = Join-Path $fixture 'fixture-cli'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $fixture 'fixture-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$checkouts = @(New-Checkout Alpha; New-Checkout Beta)
$watchers = @()
foreach ($checkout in $checkouts) {
   foreach ($scope in 'idea','dotsettings') {
      $watcher = if ($scope -eq 'idea') { [IO.FileSystemWatcher]::new("$checkout/.idea") } else { [IO.FileSystemWatcher]::new($checkout,'*.DotSettings*') }
      $watcher.IncludeSubdirectories = $scope -eq 'idea'
      $watcher.InternalBufferSize = 65536
      foreach ($event in 'Changed','Created','Deleted','Renamed','Error') {
         Register-ObjectEvent -InputObject $watcher -EventName $event -SourceIdentifier "ProjectStore.$(Split-Path -Leaf $checkout).$scope.$event"
      }
      $watcher.EnableRaisingEvents = $true
      $watchers += $watcher
   }
}
$token = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$start = @{Profile=$profileDir; RiderHome=$RiderHome; Solution=(Get-Solution $checkouts[0]); RemoteDevPort=$RemoteDevPort; RoyderPort=$RoyderPort; ToolsBuild=$ToolsBuild
   ExtraPlugin=@((Resolve-Path "$PSScriptRoot/bin/plugin/RemoteCloseWorktree").Path); ExtraEnvironment=@{ROYDER_MANAGED_HOST_TOKEN=$token}; IsolateProjectStores=($Mode -eq 'Private')}
Record-Phase '0-seeded'
$expectedSnapshot = "$fixture/0-seeded.json"
$ownerEventCount = 0
try {
   $hostState = & "$harness/RemoteDevHost.ps1" start @start
   $run = $hostState.evidence
   $builtIn = Get-AdvertisedPort $run
   $passesRealSolution = (Get-Content -LiteralPath "$run/host.args") -ccontains ('"' + $start.Solution.Replace('\','\\') + '"')
   if ($passesRealSolution -ne ($Mode -eq 'Stock')) { throw 'Unexpected first solution in the JVM startup arguments' }
   Record-Phase '1-launcher-ready'
   $tags = @{}
   foreach ($checkout in $checkouts) {
      $solution = Get-Solution $checkout
      $tags[$checkout] = Send-Management open $solution
      if (!$tags[$checkout]) { throw "Missing ETag for $solution" }
      Wait-ForFixtureReady $solution
      Record-Phase "2-open-$(Split-Path -Leaf $checkout)"
      Rename-InFixture $checkout
      Record-Phase "3-edited-$(Split-Path -Leaf $checkout)"
   }
   if ($ExerciseHandoff) {
      # Mimic an ordinary Rider saving the owner's latest preference while the private JVM still owns Alpha.
      Start-Sleep -Milliseconds 250
      if (@(Get-Event | Where-Object SourceIdentifier -like 'ProjectStore.*').Count) { throw 'Checkout settings changed before the owner edit.' }
      $workspace = Join-Path $checkouts[0] '.idea/.idea.Probe/.idea/workspace.xml'
      $original = [IO.File]::ReadAllText($workspace)
      $ownerText = $original.Replace('Unfinished work','Owner edit made after the private project opened')
      if ($ownerText -ceq $original) { throw 'Expected the original personal changelist comment' }
      [IO.File]::WriteAllText($workspace, $ownerText)
      Start-Sleep -Milliseconds 350
      $ownerEvents = @(Get-Event | Where-Object SourceIdentifier -like 'ProjectStore.*')
      if (!$ownerEvents.Count -or @($ownerEvents | Where-Object {
         $_.SourceIdentifier -ne 'ProjectStore.Alpha.idea.Changed' -or
            [IO.Path]::GetFullPath($_.SourceEventArgs.FullPath) -ne [IO.Path]::GetFullPath($workspace)
      }).Count) {
         throw 'The watcher did not attribute the expected workspace write to the fixture.'
      }
      $ownerEventCount = $ownerEvents.Count
      Record-Phase '3-owner-edit'
      $expectedSnapshot = "$fixture/3-owner-edit.json"
   }
   foreach ($checkout in $checkouts[1,0]) {
      $null = Send-Management close (Get-Solution $checkout) $tags[$checkout]
      Record-Phase "4-closed-$(Split-Path -Leaf $checkout)"
   }
} finally {
   try {
      $stopped = & "$harness/RemoteDevHost.ps1" stop -Profile $profileDir
      if ($stopped.phase -ne 'Stopped' -and $stopped.phase -ne 'Never started') { Write-Warning "Private host state: $($stopped.phase)" }
   } finally {
      Record-Phase '5-stopped'
      Start-Sleep -Milliseconds 250
      $events = @(Get-Event | Where-Object SourceIdentifier -like 'ProjectStore.*' | ForEach-Object {
         if ($_.SourceIdentifier -like '*.Error') { "$($_.SourceIdentifier) $($_.SourceEventArgs.GetException().Message)" }
         else { "$($_.TimeGenerated.ToUniversalTime().ToString('O')) $($_.SourceIdentifier) $($_.SourceEventArgs.FullPath)" }
      })
      [IO.File]::WriteAllLines("$fixture/store-events.txt", [string[]]$events)
      Get-EventSubscriber | Where-Object SourceIdentifier -like 'ProjectStore.*' | Unregister-Event
      foreach ($watcher in $watchers) { $watcher.Dispose() }
   }
}
$before = [IO.File]::ReadAllText($expectedSnapshot)
$after = [IO.File]::ReadAllText("$fixture/5-stopped.json")
$privateWorkspaces = if (Test-Path -LiteralPath "$run/project-stores") {
   @(Get-ChildItem -LiteralPath "$run/project-stores" -File -Recurse -Filter workspace.xml)
}
$privateWorkspaces = @($privateWorkspaces | Where-Object { $_ })
[pscustomobject]@{mode=$Mode; checkoutStoreEvents=$events.Count; fixtureOwnerEvents=$ownerEventCount; checkoutSnapshotsEqual=($before -ceq $after); privateWorkspaceFiles=@($privateWorkspaces | ForEach-Object FullName); evidence=$fixture}
if ($Mode -eq 'Private' -and ($events.Count -ne $ownerEventCount -or $before -cne $after -or $privateWorkspaces.Count -lt 2)) {
   throw "Private project stores failed verification. See $fixture."
}
