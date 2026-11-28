function Get-RealProfileSnapshot($RiderHome, $Backup) {
   $roaming = Join-Path $env:APPDATA JetBrains
   $local = Join-Path $env:LOCALAPPDATA JetBrains
   $product = Read-HostJson (Join-Path $RiderHome product-info.json)
   $active = Join-Path $roaming $product.dataDirectoryName
   $files = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
   foreach ($entry in Get-ChildItem -Force -LiteralPath $roaming) {
      if ($entry.PSIsContainer -and $entry.Name -match '(19|20)\d\d\.\d+') { continue }
      if ($entry.PSIsContainer) {
         foreach ($f in Get-ChildItem -File -Force -Recurse -LiteralPath $entry.FullName) { $null = $files.Add($f.FullName) }
      } else { $null = $files.Add($entry.FullName) }
   }
   foreach ($dir in 'options','keymaps','templates','codestyles') {
      $path = Join-Path $active $dir
      if (Test-Path -LiteralPath $path) {
         foreach ($f in Get-ChildItem -File -Recurse -Force -LiteralPath $path) { $null = $files.Add($f.FullName) }
      }
   }
   foreach ($f in Get-ChildItem -File -Force -LiteralPath $active) {
      if ($f.Extension -in '.xml','.txt','.properties','.DotSettings','.vmoptions') { $null = $files.Add($f.FullName) }
   }
   $gitStatus = @()
   if (Test-Path -LiteralPath (Join-Path $env:USERPROFILE '.git')) {
      $pathspec = [IO.Path]::GetRelativePath($env:USERPROFILE, $roaming)
      $tracked = git --no-optional-locks -C $env:USERPROFILE -c core.quotepath=false ls-files -- $pathspec
      if ($LASTEXITCODE) { throw 'Could not list tracked JetBrains settings.' }
      foreach ($file in $tracked) { $null = $files.Add((Join-Path $env:USERPROFILE $file)) }
      $gitStatus = @(git --no-optional-locks -C $env:USERPROFILE status --short -- $pathspec)
      if ($LASTEXITCODE) { throw 'Could not read JetBrains settings Git status.' }
   }
   $records = @{}
   foreach ($path in $files) {
      $f = [IO.FileInfo]::new($path)
      if (!$f.Exists) { continue }
      if ($f.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected link in the profile audit: $path" }
      try {
         $stream = [IO.File]::Open($path, 'Open','Read', [IO.FileShare]'ReadWrite,Delete')
         try {
            $buffer = [IO.MemoryStream]::new()
            try { $stream.CopyTo($buffer); $bytes = $buffer.ToArray() } finally { $buffer.Dispose() }
         } finally { $stream.Dispose() }
      }
      catch [IO.FileNotFoundException] { continue } # Discovery files expire while IDEs run.
      $relative = [IO.Path]::GetRelativePath($roaming, $path)
      $records[$relative] = [ordered]@{ hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
                                      bytes=$bytes.LongLength; modified=$f.LastWriteTimeUtc.Ticks }
      if ($Backup) {
         $copy = Join-Path $Backup $relative
         [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($copy)) | Out-Null
         [IO.File]::WriteAllBytes($copy, $bytes)
         [IO.File]::SetLastWriteTimeUtc($copy, $f.LastWriteTimeUtc)
      }
   }
   $entries = @{}
   foreach ($f in Get-ChildItem -Force -LiteralPath $local) {
      $entries[$f.Name] = [ordered]@{ directory=$f.PSIsContainer; created=$f.CreationTimeUtc.Ticks; modified=$f.LastWriteTimeUtc.Ticks }
      if (!$f.PSIsContainer) { $entries[$f.Name].bytes = $f.Length }
   }
   return @{ roaming=$records; localEntries=$entries; gitStatus=$gitStatus }
}

function Compare-Entries($Before, $After) {
   foreach ($key in @(@($Before.Keys) + @($After.Keys) | Sort-Object -Unique)) {
      if (!$Before.Contains($key)) { [pscustomobject]@{ path=$key; change='added' } }
      elseif (!$After.Contains($key)) { [pscustomobject]@{ path=$key; change='removed' } }
      else {
         $fields = @($Before[$key].Keys | Where-Object { $Before[$key][$_] -ne $After[$key][$_] })
         if ($fields.Count) { [pscustomobject]@{ path=$key; change=($fields -join ', ') } }
      }
   }
}

function Invoke-ProfileAudit($ProfilePath, $RiderHome, $Phase) {
   $current = Join-Path $ProfilePath audit-current.json
   if ($Phase -eq 'Before') {
      $session = [IO.Directory]::CreateDirectory((Join-Path $ProfilePath "audits/$([guid]::NewGuid().ToString('N'))")).FullName
      $snapshot = Get-RealProfileSnapshot $RiderHome (Join-Path $session backup)
      Write-HostJson (Join-Path $session before.json) $snapshot
      Write-HostJson $current $session
      return [pscustomobject]@{ phase='Before'; evidence=$session; roamingFiles=$snapshot.roaming.Count; localEntries=$snapshot.localEntries.Count }
   }
   $session = Read-HostJson $current
   $before = Read-HostJson (Join-Path $session before.json)
   $after = Get-RealProfileSnapshot $RiderHome $null
   Write-HostJson (Join-Path $session after.json) $after
   $result = [ordered]@{ phase='After'; evidence=$session; roaming=@(Compare-Entries $before.roaming $after.roaming)
                         localEntries=@(Compare-Entries $before.localEntries $after.localEntries)
                         gitStatusEqual=(($before.gitStatus -join "\n") -ceq ($after.gitStatus -join "\n")) }
   Write-HostJson (Join-Path $session comparison.json) $result
   [pscustomobject]$result
}
