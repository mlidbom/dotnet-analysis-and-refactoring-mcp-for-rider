#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PrimaryExecutable,
      [Parameter(Mandatory)][string]$SettingsFile,
      [switch]$Serve)

$ErrorActionPreference = 'Stop'
$executable = (Get-Item -LiteralPath $PrimaryExecutable).FullName
$config = (Get-Item -LiteralPath $SettingsFile).FullName
$folder = Split-Path -Parent $config
if ($Serve) {
   $logs = [IO.Directory]::CreateDirectory((Join-Path $folder logs)).FullName
   $log = Join-Path $logs ("primary-" + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + ".log")
   [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore', 'Warning', 'Process')
   & $executable serve $config >> $log 2>&1
   exit $LASTEXITCODE
}

$name = 'RoyderPrimary'
$description = "Run this user's standalone Royder MCP primary at sign-in."
$existing = Get-ScheduledTask -TaskName $name -TaskPath '\' -ErrorAction SilentlyContinue
if ($existing -and $existing.Description -ne $description) { throw "A different scheduled task already owns $name." }
$runner = Join-Path $folder 'Run-RoyderPrimary.ps1'
Copy-Item -LiteralPath $PSCommandPath -Destination $runner -Force
$arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -File "{0}" -PrimaryExecutable "{1}" -SettingsFile "{2}" -Serve' -f $runner,$executable,$config
$windowsPowerShell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$action = New-ScheduledTaskAction -Execute $windowsPowerShell -Argument $arguments -WorkingDirectory $folder
$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName $name -TaskPath '\' -Description $description -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Select-Object TaskName,State
