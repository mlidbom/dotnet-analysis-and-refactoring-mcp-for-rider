#requires -Version 7
param([switch]$UseJbLauncher)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path "$PSScriptRoot/../..").Path
$solution = Join-Path $repository 'ReSharperMcp.sln'
$version = '2026.3.0.1'
$package = 'jetbrains.resharper.globaltools'
$jb = (Get-Command jb -CommandType Application).Source
$jbDirectory = Join-Path (Split-Path $jb) ".store/$package/$version/$package/$version/tools/net10.0/any"
$worktree = Split-Path $repository -Leaf
$scratch = Join-Path ([IO.Path]::GetTempPath()) "royder-headless/$worktree/extension-$([Guid]::NewGuid().ToString('N'))"
$buildName = 'extension-check'
$toolsBuild = "$scratch/tools/$buildName"
foreach ($folder in 'feed', 'local', 'roaming', 'temp', 'caches', 'logs', "tools/$buildName") {
   [void][IO.Directory]::CreateDirectory("$scratch/$folder")
}
Write-Host "Evidence directory: $scratch"

$hostName = 'DotNetAnalysisAndRefactoringMcp'
$toolsName = "$hostName.Tools"
dotnet build "$repository/src/$toolsName" -c Debug --nologo
if ($LASTEXITCODE -ne 0) { throw 'Could not build the Royder host and tools.' }
dotnet pack "$PSScriptRoot/Royder.HeadlessExtension.csproj" -c Debug "-p:JbDirectory=$jbDirectory" -o "$scratch/feed" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Could not pack the InspectCode extension.' }
$hostAssembly = "$repository/src/$hostName/bin/Debug/net472/$hostName.dll"
$hostHash = (Get-FileHash -LiteralPath $hostAssembly).Hash.ToLowerInvariant()
Copy-Item -LiteralPath "$repository/src/$toolsName/bin/Debug/net472/$toolsName.dll" -Destination $toolsBuild
[IO.File]::WriteAllText("$toolsBuild/host.sha256", $hostHash)

$port = 23900
if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains $port) {
   throw "Port $port is already in use."
}
if ($UseJbLauncher) {
   $start = [Diagnostics.ProcessStartInfo]::new($jb)
   $start.ArgumentList.Add('inspectcode')
} else {
   $runtime = dotnet --list-runtimes | ForEach-Object {
      if ($_ -match '^Microsoft\.WindowsDesktop\.App (10\.0\.\d+) ') { [version]$Matches[1] }
   } | Sort-Object -Descending | Select-Object -First 1
   if (!$runtime) { throw 'Install the .NET 10 Windows Desktop runtime to run the probe.' }
   $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet -CommandType Application).Source)
   foreach ($argument in @('exec', '--fx-version', "$runtime", '--roll-forward', 'LatestPatch',
                           '--runtimeconfig', "$jbDirectory/inspectcode.runtimeconfig.json", "$jbDirectory/inspectcode.exe")) {
      $start.ArgumentList.Add($argument)
   }
}
$start.WorkingDirectory = $repository
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT'] = [string]$port
$start.Environment['DOTNET_ANALYSIS_AND_REFACTORING_MCP_TOOLS'] = "$scratch/tools"
$start.Environment['ROYDER_PROBE_DIRECTORY'] = $scratch
$start.Environment['JET_LOCAL_APPDATA_DIR'] = "$scratch/local"
$start.Environment['JET_ROAMING_APPDATA_DIR'] = "$scratch/roaming"
$start.Environment['JET_TEMP_DIR'] = "$scratch/temp"
$start.Environment['TEMP'] = "$scratch/temp"
$start.Environment['TMP'] = "$scratch/temp"
foreach ($argument in @(
   $solution, '--no-build', '--no-swea', '--no-updates', '--jobs=1', '--include=**/ZoneMarker.cs',
   '-x=Royder.HeadlessExtension', "--source=$scratch/feed", "--caches-home=$scratch/caches",
   "--LogFolder=$scratch/logs", "--output=$scratch/issues.sarif"
)) {
   $start.ArgumentList.Add($argument)
}

$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
try {
   $timer = [Diagnostics.Stopwatch]::StartNew()
   while (!(Test-Path -LiteralPath "$scratch/waiting.txt")) {
      if ($process.HasExited) { throw "InspectCode exited with $($process.ExitCode) before the report consumer ran. See $scratch/logs." }
      if ($timer.Elapsed.TotalMinutes -ge 3) { throw 'Timed out opening the solution.' }
      Start-Sleep -Milliseconds 200
   }
   $marker = Get-Content -LiteralPath "$scratch/shell.txt"
   Write-Host $marker
   if (!$UseJbLauncher -and $marker -notmatch 'on CLR 10\.') { throw 'The check requires .NET 10.' }
   if ((Get-Content "$scratch/zones.txt") -notcontains 'DaemonZone') { throw 'DaemonZone is missing.' }
   Get-Content -LiteralPath "$scratch/waiting.txt"
   $health = Invoke-RestMethod "http://127.0.0.1:$port/health" -TimeoutSec 8 -NoProxy
   $health | ConvertTo-Json -Depth 10 | Set-Content "$scratch/health.json"
   $registered = @($health.solutions)
   if ($registered.Count -ne 1 -or $registered[0].path -ne $solution -or $registered[0].tools.host -ne $hostHash -or
       $registered[0].tools.build -ne $buildName -or $registered[0].tools.folder -ne [IO.Path]::GetFullPath("$scratch/tools")) {
      throw "Unexpected solution or tools build. See $scratch/health.json."
   }
   Write-Host "Royder registered $solution on port $port."
   $request = @{
      jsonrpc = '2.0'
      id = 1
      method = 'tools/call'
      params = @{
         name = 'list_symbols_in_file'
         arguments = @{ filePath = "$repository/src/$hostName/McpShellComponent.cs" }
      }
   } | ConvertTo-Json -Depth 4
   $answer = Invoke-RestMethod "http://127.0.0.1:$port/" -Method Post -Body $request -ContentType 'application/json' -TimeoutSec 12 -NoProxy
   $answer | ConvertTo-Json -Depth 10 | Set-Content "$scratch/symbols.json"
   $declarations = $answer.result.content.text -join [Environment]::NewLine
   if ($answer.error -or $answer.result.isError -or $declarations -notmatch '(?m)^\s+method WatchdogPing\(') {
      throw "Symbol query failed. See $scratch/symbols.json."
   }
   Write-Host 'list_symbols_in_file found McpShellComponent.WatchdogPing.'
} finally {
   [IO.File]::WriteAllText("$scratch/stop", '')
   if (!$process.WaitForExit(30000)) {
      $process.Kill($true)
      $process.WaitForExit()
   }
   [IO.File]::WriteAllText("$scratch/logs/stdout.txt", $stdout.GetAwaiter().GetResult())
   [IO.File]::WriteAllText("$scratch/logs/stderr.txt", $stderr.GetAwaiter().GetResult())
}
if ($process.ExitCode -ne 0) { throw "InspectCode exited with $($process.ExitCode)." }
if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains $port) {
   throw "Port $port still has a listener after InspectCode exited."
}
Write-Host "InspectCode exited and released port $port. Report: $((Get-Item "$scratch/issues.sarif").FullName)"
