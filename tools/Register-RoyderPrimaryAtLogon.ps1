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
   # In the encoding of the >> below, Windows PowerShell's UTF-16.
   function Write-LogLine([string]$Line) { $Line | Out-File -LiteralPath $log -Append -Encoding unicode }
   $machineStart = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
   Write-LogLine "Started by the $name task at $(Get-Date -Format o); the machine started at $($machineStart.ToString('o'))."
   [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore', 'Warning', 'Process')
   & $executable serve $config >> $log 2>&1
   $exitCode = $LASTEXITCODE
   # The headless console host below ends with 0 whatever its program's exit code, so the task's last result doesn't tell it.
   Write-LogLine "Ended with exit code $exitCode at $(Get-Date -Format o)."
   exit $exitCode
}

$description = "Run this user's standalone Royder MCP primary at sign-in."
$existing = Get-ScheduledTask -TaskName $name -TaskPath '\' -ErrorAction SilentlyContinue
if ($existing -and $existing.Description -ne $description) { throw "A different scheduled task already owns $name." }
$runner = Join-Path $folder 'Run-RoyderPrimary.ps1'
Copy-Item -LiteralPath $PSCommandPath -Destination $runner -Force
$windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
# A console in a console host of its own, with no window. Started without one, the console goes to the default terminal,
# Windows Terminal on Magnus's machine, whose window -WindowStyle Hidden only minimizes. Closing that window ended the
# primary with 0xC000013A, twice on 2026-11-23, and every session went without Royder.
$headlessConsoleHost = Join-Path $env:SystemRoot 'System32\conhost.exe'
$arguments = '--headless "{0}" -NoProfile -NonInteractive -File "{1}" -PrimaryExecutable "{2}" -SettingsFile "{3}" -Serve' -f $windowsPowerShell,$runner,$executable,$config
$action = New-ScheduledTaskAction -Execute $headlessConsoleHost -Argument $arguments -WorkingDirectory $folder
$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$atSignIn = New-ScheduledTaskTrigger -AtLogOn -User $user
# Task Scheduler's restart settings below cover only a start that fails: a primary that ends after it started stays ended.
# So a second trigger fires every minute from now on, across restarts of the machine, and starts it again within a minute
# of its end. A sign-in trigger's own repetition would begin only at the next sign-in. While it runs, IgnoreNew makes each
# firing do nothing; while the user isn't signed in, the interactive principal does. To keep it stopped, disable the task.
$everyMinute = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 1)
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName $name -TaskPath '\' -Description $description -Action $action -Trigger $atSignIn,$everyMinute -Principal $principal -Settings $settings -Force | Select-Object TaskName,State
