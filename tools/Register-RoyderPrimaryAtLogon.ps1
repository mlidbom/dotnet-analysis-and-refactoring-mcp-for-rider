#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PrimaryExecutable,
      [Parameter(Mandatory)][string]$SettingsFile,
      [switch]$Serve)

$ErrorActionPreference = 'Stop'
$name = 'RoyderPrimary'
$executable = (Get-Item -LiteralPath $PrimaryExecutable).FullName
$config = (Get-Item -LiteralPath $SettingsFile).FullName
$folder = Split-Path -Parent $config
if ($Serve) {
   $logs = [IO.Directory]::CreateDirectory((Join-Path $folder logs)).FullName
   $log = Join-Path $logs ("primary-" + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + ".log")
   # A start long after the machine's, with no update just before it, means the run before ended unexpectedly in the minute before.
   $machineStart = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
   # In the encoding of the >> below, Windows PowerShell's UTF-16.
   "Started by the $name task at $(Get-Date -Format o); the machine started at $($machineStart.ToString('o'))." | Out-File -LiteralPath $log -Encoding unicode
   [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore', 'Warning', 'Process')
   & $executable serve $config >> $log 2>&1
   exit $LASTEXITCODE
}

$description = "Run this user's standalone Royder MCP primary at sign-in."
$existing = Get-ScheduledTask -TaskName $name -TaskPath '\' -ErrorAction SilentlyContinue
if ($existing -and $existing.Description -ne $description) { throw "A different scheduled task already owns $name." }
$runner = Join-Path $folder 'Run-RoyderPrimary.ps1'
Copy-Item -LiteralPath $PSCommandPath -Destination $runner -Force
$arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -File "{0}" -PrimaryExecutable "{1}" -SettingsFile "{2}" -Serve' -f $runner,$executable,$config
$windowsPowerShell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$action = New-ScheduledTaskAction -Execute $windowsPowerShell -Argument $arguments -WorkingDirectory $folder
$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$atSignIn = New-ScheduledTaskTrigger -AtLogOn -User $user
# Task Scheduler's restart settings below cover only a start that fails: a primary that ends after it started stays ended
# (2026-11-23: one ended with 0xC000013A some time after sign-in, and every session went without Royder). So a second
# trigger fires every minute from now on, across restarts of the machine, and starts it again within a minute of its end.
# A sign-in trigger's own repetition would begin only at the next sign-in. While it runs, IgnoreNew makes each firing do
# nothing; while the user isn't signed in, the interactive principal does. To keep it stopped, disable the task first.
$everyMinute = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 1)
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName $name -TaskPath '\' -Description $description -Action $action -Trigger $atSignIn,$everyMinute -Principal $principal -Settings $settings -Force | Select-Object TaskName,State
