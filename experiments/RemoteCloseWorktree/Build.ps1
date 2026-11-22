#requires -Version 7.0
param(
   [string] $RiderHome = (Join-Path $env:LOCALAPPDATA 'Programs/Rider'),
   [switch] $Verify,
   [string] $VerifierJar
)

$ErrorActionPreference = 'Stop'
$RiderHome = (Resolve-Path -LiteralPath $RiderHome).Path
$expectedBuild = 'RD-263.7307.325'
if ((Get-Content -Raw -LiteralPath "$RiderHome/build.txt").Trim() -ne $expectedBuild) {
   throw "This experiment targets $expectedBuild. Check -RiderHome."
}

$objectDir = [IO.Directory]::CreateDirectory("$PSScriptRoot/obj").FullName
$runId = [Guid]::NewGuid().ToString('N')
$stage = [IO.Directory]::CreateDirectory((Join-Path $objectDir $runId)).FullName
$pluginLib = [IO.Directory]::CreateDirectory("$PSScriptRoot/bin/plugin/RemoteCloseWorktree/lib").FullName
$jar = Join-Path $pluginLib 'RemoteCloseWorktree.jar'
try {
   $classes = [IO.Directory]::CreateDirectory("$stage/classes").FullName
   $sources = (Get-ChildItem "$PSScriptRoot/src/main/java" -Recurse -File -Filter '*.java').FullName
   & "$RiderHome/jbr/bin/javac.exe" -proc:none --release 21 -encoding UTF-8 -classpath "$RiderHome/lib/*" -d $classes $sources
   if ($LASTEXITCODE -ne 0) { throw "javac exited with $LASTEXITCODE" }
   $testClasses = [IO.Directory]::CreateDirectory("$stage/test-classes").FullName
   $specs = (Get-ChildItem "$PSScriptRoot/src/test/java" -Recurse -File -Filter '*.java').FullName
   & "$RiderHome/jbr/bin/javac.exe" -proc:none --release 21 -encoding UTF-8 -classpath "$classes;$RiderHome/lib/*" -d $testClasses $specs
   if ($LASTEXITCODE -ne 0) { throw "Specification compilation failed with $LASTEXITCODE" }
   & "$RiderHome/jbr/bin/java.exe" -XX:-UsePerfData -cp "$classes;$testClasses" dev.magnus.royder.remotecloseworktree.WorktreeDiskIdentitySpec $stage
   if ($LASTEXITCODE -ne 0) { throw "Worktree disk identity specification failed with $LASTEXITCODE" }
   & "$RiderHome/jbr/bin/java.exe" -XX:-UsePerfData -cp "$classes;$testClasses;$RiderHome/lib/*" dev.magnus.royder.remotecloseworktree.AbandonedProjectSpec
   if ($LASTEXITCODE -ne 0) { throw "Abandoned project specification failed with $LASTEXITCODE" }
   Copy-Item -LiteralPath "$PSScriptRoot/src/main/resources/META-INF" -Destination $classes -Recurse
   $builtJar = Join-Path $stage 'RemoteCloseWorktree.jar'
   [IO.Compression.ZipFile]::CreateFromDirectory($classes, $builtJar)
   [IO.File]::Move($builtJar, $jar, $true)
} finally {
   if ((Split-Path -Parent $stage) -ne $objectDir -or (Split-Path -Leaf $stage) -ne $runId) {
      throw "Unexpected build staging directory: $stage"
   }
   Remove-Item -LiteralPath $stage -Recurse -Force
}
Write-Output "Built $jar"
if (!$Verify) { return }

if (!$VerifierJar) {
   $cache = "$env:USERPROFILE/.gradle/caches/modules-2/files-2.1/org.jetbrains.intellij.plugins/verifier-cli/1.411"
   $VerifierJar = (Get-ChildItem -LiteralPath $cache -Recurse -Filter 'verifier-cli-1.411-all.jar' -ErrorAction SilentlyContinue |
                      Select-Object -First 1).FullName
   if (!$VerifierJar) { throw 'Pass -VerifierJar with the path to a local Plugin Verifier CLI JAR.' }
}
$VerifierJar = (Resolve-Path -LiteralPath $VerifierJar).Path
$state = [IO.Directory]::CreateDirectory("$objectDir/verifier/$runId").FullName
foreach ($folder in 'home', 'local', 'roaming', 'tmp') {
   $null = [IO.Directory]::CreateDirectory("$state/$folder")
}

$verifier = [Diagnostics.ProcessStartInfo]::new("$RiderHome/jbr/bin/java.exe")
$verifier.UseShellExecute = $false
$verifier.WorkingDirectory = $state
$verifier.Environment['USERPROFILE'] = "$state/home"
$verifier.Environment['APPDATA'] = "$state/roaming"
$verifier.Environment['LOCALAPPDATA'] = "$state/local"
$verifier.Environment['TEMP'] = $verifier.Environment['TMP'] = "$state/tmp"
foreach ($argument in @('-XX:-UsePerfData', '-Xmx2g', "-Duser.home=$state/home", "-Djava.io.tmpdir=$state/tmp",
                        "-Dplugin.verifier.home.dir=$state/home/verifier", '-jar', $VerifierJar,
                        'check-plugin', $jar, $RiderHome, '-offline', '-runtime-dir', "$RiderHome/jbr",
                        '-verification-reports-dir', "$state/reports", '-verification-reports-formats', 'plain')) {
   $verifier.ArgumentList.Add($argument)
}
$process = [Diagnostics.Process]::Start($verifier)
try {
   $process.WaitForExit()
   if ($process.ExitCode -ne 0) { throw "Plugin Verifier exited with $($process.ExitCode). See $state/reports" }
} finally {
   $process.Dispose()
}
$reports = @(Get-ChildItem -LiteralPath "$state/reports" -Recurse -File -Filter 'verification-verdict.txt')
if ($reports.Count -ne 1) { throw "Expected one verifier verdict under $state/reports" }
$verdict = (Get-Content -Raw -LiteralPath $reports[0].FullName).Trim()
Write-Output "Verifier verdict: $verdict ($($reports[0].FullName))"
if ($verdict -cne 'Compatible') { throw 'Plugin Verifier did not report a clean compatibility verdict.' }
