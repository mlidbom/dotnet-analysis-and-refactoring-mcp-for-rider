#requires -Version 7.4
param([Parameter(Mandatory)][ValidateSet('start','stop','open','close','discard','status')][string]$Operation,
      [Parameter(Mandatory)][string]$Profile, [string]$Solution, [string]$Generation,
      [string]$RiderHome, [string]$ToolsBuild, [int]$RemoteDevPort, [int]$RoyderPort, [int]$HeapMiB,
      [Parameter(Mandatory)][string]$ResultFile)
. "$PSScriptRoot/Common.ps1"

function Get-AdvertisedPort([string]$Run) {
   $cache = Join-Path $Run builtin-port.json
   if ([IO.File]::Exists($cache)) { return [int](Read-HostJson $cache) }
   foreach ($log in (Get-ChildItem -LiteralPath (Join-Path $Run log) -File -Filter 'idea*.log' | Sort-Object LastWriteTimeUtc -Descending)) {
      $announcement = Select-String -LiteralPath $log.FullName -Pattern '^\d{4}-\d{2}-\d{2} .* INFO - #o\.j\.i\.BuiltInServerManager - built-in server started, port (\d+)\s*$' | Select-Object -Last 1
      if ($announcement) {
         $port = [int]$announcement.Matches[0].Groups[1].Value
         Write-HostJson $cache $port
         return $port
      }
   }
   throw "The private JVM has not announced its IntelliJ built-in HTTP port in $Run/log."
}

function Invoke-ManagedOperation {
$profileDir = Get-ProfilePath $Profile -Managed
$assets = Split-Path -Parent $PSScriptRoot
if ($Operation -eq 'start') {
   $null = & "$PSScriptRoot/RemoteDevHost.ps1" start -Profile $profileDir -Solution $Solution -ManagedAssets $assets -RiderHome $RiderHome -ToolsBuild $ToolsBuild -RemoteDevPort $RemoteDevPort -RoyderPort $RoyderPort -HeapMiB $HeapMiB
   $null = Get-AdvertisedPort (Read-HostJson (Join-Path $profileDir current.json))
   return
}

$run = Read-HostJson (Join-Path $profileDir current.json)
if ($run -ne $Generation) { throw 'The private Rider generation changed before the operation.' }
if ($Operation -eq 'stop') {
   $null = & "$PSScriptRoot/RemoteDevHost.ps1" stop -Profile $profileDir -ManagedAssets $assets
   return
}
$state = Get-RunState $run
$jvm = Get-LiveIdentity $state.hostProcess
if ($state.phase -ne 'Running' -or !$jvm -or !(Get-LiveIdentity $state.supervisor)) { throw 'Private Rider is no longer running.' }
if ([IO.File]::ReadAllText((Join-Path $profileDir config/.lock)).Trim() -ne [string]$jvm.Id) { throw 'Private Rider profile lock mismatch.' }

$port = Get-AdvertisedPort $run
$path = switch ($Operation) { open {'royder-open-worktree'} close {'royder-close-worktree'} discard {'royder-close-worktree/discard'} status {'royder-open-worktree/status'} }
$key = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Solution.ToUpperInvariant())))
$tagFile = Join-Path $run "project-$key.etag"
$tag = if ([IO.File]::Exists($tagFile)) { Read-HostJson $tagFile }
$intentFile = Join-Path $run "project-$key.open-id"
if ($Operation -eq 'open' -and ![IO.File]::Exists($intentFile)) { Write-HostJson $intentFile ([guid]::NewGuid().ToString('N')) }
$openId = if ($Operation -in 'open','status' -and [IO.File]::Exists($intentFile)) { Read-HostJson $intentFile } else { '' }
if ($Operation -in 'close','discard' -and !$tag) { throw 'Missing the original IntelliJ project instance tag. Close was not dispatched.' }
$client = [Net.Sockets.TcpClient]::new()
try {
   Add-Type -Path "$PSScriptRoot/ConnectedRiderRequest.cs"
   $null = $client.ConnectAsync([Net.IPAddress]::Loopback, $port).WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult()
   $peer = Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort $port -RemoteAddress 127.0.0.1 -RemotePort $client.Client.LocalEndPoint.Port -State Established -ErrorAction Stop
   if ($peer.OwningProcess -ne $jvm.Id -or !(Get-LiveIdentity $state.hostProcess)) { throw 'Built-in HTTP connection does not belong to the private JVM.' }
   $encrypted = [IO.File]::ReadAllBytes((Join-Path $run host-token.dpapi))
   $token = [Text.Encoding]::ASCII.GetString([Security.Cryptography.ProtectedData]::Unprotect($encrypted, $null, 'CurrentUser'))
   $ifMatch = if ($Operation -ne 'open') { $tag } else { '' }
   $reply = [RemoteDevHost.ConnectedRiderRequest]::PostAsync($client, [uri]"http://127.0.0.1:$port/api/$path", $token, $Solution, $ifMatch, $openId).GetAwaiter().GetResult()
   if (($Operation -eq 'open' -or ($Operation -eq 'status' -and $openId -and $reply.Status -eq 200)) -and $reply.ETag) {
      if ($tag -and $tag -cne $reply.ETag) { throw 'Rider returned a different project instance at the same path. The original close tag has been preserved.' }
      Write-HostJson $tagFile $reply.ETag
   }
   if ($Operation -eq 'open' -and $reply.Status -eq 200 -and !$reply.ETag) { throw 'Rider open returned no strong project instance ETag.' }
   if ($reply.Status -ne 200 -and !($reply.Status -eq 404 -and $Operation -ne 'open')) {
      throw "Private Rider $Operation returned HTTP $($reply.Status): $($reply.Message)"
   }
   $reply.Status
} finally { $client.Dispose() }
}

# A detached Windows supervisor may inherit its initiating shell's output handles.
# Use a private one-shot result file so the manager never waits for a surviving child's pipe EOF.
$commandLease = $null
try {
   $owner = $env:ROYDER_MANAGED_OWNER | ConvertFrom-Json -AsHashtable
   $commandLease = [IO.File]::Open((Join-Path $Profile 'managed-command.lock'), 'OpenOrCreate','ReadWrite','None')
   if (!(Get-LiveIdentity $owner)) { throw 'The requesting primary ended before its Rider command could be dispatched.' }
   $answer = Invoke-ManagedOperation
   Write-HostJson $ResultFile @{ ok=$true; status=$answer }
} catch {
   Write-HostJson $ResultFile @{ ok=$false; error=$_.Exception.Message }
   exit 1
} finally { if ($commandLease) { $commandLease.Dispose() } }
