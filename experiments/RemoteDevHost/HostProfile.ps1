function Copy-PluginTree([string]$Source, [string]$Target) {
   # A fresh destination per run avoids merging plugin versions or old tools builds.
   if (Test-Path -LiteralPath $Target) { throw "Two plugins or builds use the same destination: $Target" }
   [IO.Directory]::CreateDirectory($Target) | Out-Null
   foreach ($item in Get-ChildItem -Force -LiteralPath $Source) {
      if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected link in plugin or tools build: $($item.FullName)" }
      $dest = Join-Path $Target $item.Name
      if ($item.PSIsContainer) { Copy-PluginTree $item.FullName $dest }
      else {
         [IO.File]::Copy($item.FullName, $dest)
         if ((Get-FileHash -LiteralPath $item.FullName).Hash -ne (Get-FileHash -LiteralPath $dest).Hash) {
            throw "Copy hash differs for $dest"
         }
      }
   }
}

$SettingsKey = 'http://schemas.microsoft.com/winfx/2006/xaml'
$InjectedLayers = '/Default/Environment/InjectedLayers/'

function Get-SettingsEntries([string]$File) {
   $document = [xml]::new()
   $document.Load($File)
   foreach ($entry in $document.DocumentElement.ChildNodes) {
      if ($entry.NodeType -eq 'Element') { [pscustomobject]@{ Key = $entry.GetAttribute('Key', $SettingsKey); Value = $entry.InnerText } }
   }
}

function Get-EverydayGlobalSettingsLayers([string]$DataDirectoryName) {
   # The everyday Rider's global settings file, then the files it injects, such as Magnus's personal settings, in the
   # order they apply. The private Rider injects the same, so that a severity is the same whichever Rider answers: until
   # 2026-11-25 it had none of them, and agents got warnings in its worktrees that Magnus's Rider doesn't show.
   $global = [IO.Path]::GetFullPath((Join-Path $env:APPDATA "JetBrains/$DataDirectoryName/resharper-host/GlobalSettingsStorage.DotSettings"))
   if (![IO.File]::Exists($global)) { return }
   $injections = [ordered]@{}
   foreach ($entry in Get-SettingsEntries $global) {
      if ($entry.Key -match "^$InjectedLayers(FileInjectedLayer/=|InjectedLayerCustomization/=File)(\w+)/(\w+)/@EntryValue$") {
         if (!$injections.Contains($Matches[2])) { $injections[$Matches[2]] = @{ AbsolutePath = ''; RelativePath = ''; RelativePriority = [double]::MaxValue; IsOn = 'True' } }
         $injections[$Matches[2]][$Matches[3]] = $entry.Value
      }
   }
   $global
   $injections.Values | Where-Object { $_.IsOn -eq 'True' } | Sort-Object { [double]$_.RelativePriority } | ForEach-Object {
      # As ReSharper's RegisterInjectedMounts resolves it: a relative path wins over the absolute one, and starts from the
      # hosting file itself, as if it were a folder.
      $path = if ($_.RelativePath -and ![IO.Path]::IsPathRooted($_.RelativePath)) { [IO.Path]::Combine($global, $_.RelativePath) } else { $_.AbsolutePath }
      if ($path -and [IO.File]::Exists($path)) { [IO.Path]::GetFullPath($path) }
   }
}

function Write-InjectedGlobalSettings([string]$ConfigFolder, [string[]]$Layers) {
   # The private Rider's own global settings file: ReSharper writes it, and it keeps its settings. Its injections are
   # replaced by the layers given, the first applying over the next, as an injected file is mounted below its host.
   $file = Join-Path ([IO.Directory]::CreateDirectory((Join-Path $ConfigFolder resharper-host)).FullName) GlobalSettingsStorage.DotSettings
   $system = 'clr-namespace:System;assembly=mscorlib'
   $document = [xml]::new()
   $document.PreserveWhitespace = $true
   if ([IO.File]::Exists($file)) { $document.Load($file) }
   else {
      $document.LoadXml('<wpf:ResourceDictionary xml:space="preserve" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" ' +
                        'xmlns:s="clr-namespace:System;assembly=mscorlib" xmlns:ss="urn:shemas-jetbrains-com:settings-storage-xaml" ' +
                        'xmlns:wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation"></wpf:ResourceDictionary>')
   }
   $root = $document.DocumentElement
   foreach ($entry in @($root.ChildNodes | Where-Object { $_.NodeType -eq 'Element' -and $_.GetAttribute('Key', $SettingsKey).StartsWith($InjectedLayers) })) {
      $indent = $entry.PreviousSibling
      if ($indent -and $indent.NodeType -in 'Whitespace','SignificantWhitespace') { $null = $root.RemoveChild($indent) }
      $null = $root.RemoveChild($entry)
   }
   $priority = 0
   foreach ($layer in $Layers) {
      $id = [Convert]::ToHexString([Security.Cryptography.MD5]::HashData([Text.Encoding]::UTF8.GetBytes($layer.ToUpperInvariant())))
      $priority++
      $entries = [ordered]@{
         "${InjectedLayers}FileInjectedLayer/=$id/AbsolutePath/@EntryValue" = 'String', $layer
         "${InjectedLayers}FileInjectedLayer/=$id/@KeyIndexDefined" = 'Boolean', 'True'
         "${InjectedLayers}InjectedLayerCustomization/=File$id/@KeyIndexDefined" = 'Boolean', 'True'
         "${InjectedLayers}InjectedLayerCustomization/=File$id/RelativePriority/@EntryValue" = 'Double', [string]$priority
      }
      foreach ($key in $entries.Keys) {
         $element = $document.CreateElement('s', $entries[$key][0], $system)
         $attribute = $document.CreateAttribute('x', 'Key', $SettingsKey)
         $attribute.Value = $key
         $null = $element.Attributes.Append($attribute)
         $element.InnerText = $entries[$key][1]
         $null = $root.AppendChild($document.CreateWhitespace("`n`t"))
         $null = $root.AppendChild($element)
      }
   }
   $document.Save($file)
}

function New-HostPlan($ProfilePath, $Run, $Solution, $RiderHome, $ToolsBuild, $RemoteDevPort, $RoyderPort, $HeapMiB, $ExtraPlugin, $ManagedAssets, [bool]$IsolateProjectStores = $false, [string]$ToolsFolder) {
   $solutionFile = Get-Item -LiteralPath $Solution
   if ($solutionFile.PSIsContainer -or $solutionFile.Extension -notin '.sln','.slnx') { throw "Expected a solution file: $Solution" }
   if ($ToolsBuild -in '.', '..' -or $ToolsBuild -notmatch '^[A-Za-z0-9_.-]+$') { throw 'ToolsBuild must name one deployed build directory.' }
   $install = (Get-Item -LiteralPath $RiderHome).FullName
   $product = Read-HostJson (Join-Path $install product-info.json)
   $launch = @($product.launch | Where-Object { $_.os -eq 'Windows' -and $_.arch -eq 'amd64' }) | Select-Object -First 1
   if (!$launch) { throw 'This harness requires the Windows amd64 Rider launch configuration.' }
   $plan = @{ profile = $ProfilePath; run = $Run; solution = $solutionFile.FullName; remoteDevPort = $RemoteDevPort
              royderPort = $RoyderPort; heapMiB = $HeapMiB; build = $ToolsBuild; java = (Join-Path $install $launch.javaExecutablePath)
              tools = (Join-Path $Run tools); hostHash = ''; product = $product.buildNumber }
   $ports = @(Get-ExperimentPorts $plan)
   if (($ports | Sort-Object -Unique).Count -ne $ports.Count -or
       @($ports | Where-Object { $_ -in (23751..23760) -or $_ -in 23841,23842 }).Count) {
      throw 'The RD port and the ten Royder ports must be disjoint and outside 23751–23760 and 23841–23842.'
   }
   Assert-PortsFree $plan
   $folders = @{}
   foreach ($name in 'config','system','home','temp','local','roaming','resharper-local','resharper-roaming','cli','packages','nuget-http','nuget-plugins') {
      $folders[$name] = [IO.Directory]::CreateDirectory((Join-Path $ProfilePath $name)).FullName
   }
   foreach ($name in 'plugins','log') { $folders[$name] = [IO.Directory]::CreateDirectory((Join-Path $Run $name)).FullName }
   # A shared folder of tools builds, such as the everyday Riders', is served from like theirs: its newest build for this
   # host. The pinned build is then the plugin's own, as an installed plugin's is, served while the shared folder has none.
   $folders.tools = [IO.Directory]::CreateDirectory($(if ($ToolsFolder) { [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($ToolsFolder)) } else { Join-Path $Run tools })).FullName
   $plan.tools = $folders.tools
   $plugin = 'DotNetAnalysisAndRefactoringMcp'
   $installedPlugin = Join-Path $env:APPDATA "JetBrains/$($product.dataDirectoryName)/plugins/$plugin"
   $toolsSource = Join-Path $env:LOCALAPPDATA "$plugin/tools/$ToolsBuild"
   if ($ManagedAssets) {
      $package = Read-HostJson (Join-Path $ManagedAssets package.json)
      if ($package.riderBuild -cne $product.buildNumber) {
         throw "Rider was updated to $($product.buildNumber), and the primary's package was built for $($package.riderBuild): " +
               'on-demand opens wait for a package built for the new Rider (tools/New-RoyderPrimaryPackage.ps1 -Install).'
      }
      if ($package.build -cne $ToolsBuild) { throw 'Staged tools do not match the configured pin.' }
      $installedPlugin = Join-Path $ManagedAssets "plugins/$plugin"
      $toolsSource = Join-Path $ManagedAssets "tools/$ToolsBuild"
      $ExtraPlugin = @('RemoteCloseWorktree','RemoteRoslynPatcher' | ForEach-Object { Join-Path $ManagedAssets "plugins/$_" })
      $plan.managedAssets = $ManagedAssets
   }
   Copy-PluginTree $installedPlugin (Join-Path $folders.plugins $plugin)
   foreach ($source in $ExtraPlugin) {
      $dir = Get-Item -LiteralPath $source
      if (!$dir.PSIsContainer) { throw "Expected an extracted plugin directory: $source" }
      Copy-PluginTree $dir.FullName (Join-Path $folders.plugins $dir.Name)
   }
   $buildDir = if ($ToolsFolder) { Join-Path $folders.plugins "$plugin/tools/$ToolsBuild" } else { Join-Path $folders.tools $ToolsBuild }
   Copy-PluginTree $toolsSource $buildDir
   $plan.hostHash = (Get-FileHash -LiteralPath (Join-Path $folders.plugins "$plugin/dotnet/$plugin.dll")).Hash.ToLowerInvariant()
   if (([IO.File]::ReadAllText((Join-Path $buildDir host.sha256))).Trim() -ne $plan.hostHash) { throw 'Deployed build and copied host have different host.sha256.' }
   if ($ManagedAssets -and ($package.hostHash -ne $plan.hostHash -or
       $package.toolsHash -ne (Get-FileHash -LiteralPath (Join-Path $buildDir "$plugin.Tools.dll")).Hash -or
       $package.lifecycleHash -ne (Get-FileHash -LiteralPath (Join-Path $folders.plugins 'RemoteCloseWorktree/lib/RemoteCloseWorktree.jar')).Hash -or
       $package.patcherHash -ne (Get-FileHash -LiteralPath (Join-Path $folders.plugins 'RemoteRoslynPatcher/dotnet/Royder.RemoteRoslynPatcher.dll')).Hash)) {
      throw 'A packaged plugin or tools assembly no longer matches the staged pin.'
   }
   $plan.globalSettingsLayers = @(Get-EverydayGlobalSettingsLayers $product.dataDirectoryName)
   Write-InjectedGlobalSettings $folders.config $plan.globalSettingsLayers
   $optionsDir = [IO.Directory]::CreateDirectory((Join-Path $folders.config options)).FullName
   [IO.File]::WriteAllText((Join-Path $optionsDir updates.xml), '<application><component name="UpdatesConfigurable"><option name="CHECK_NEEDED" value="false"/><option name="PLUGINS_CHECK_NEEDED" value="false"/><option name="PLUGINS_AUTO_UPDATE" value="false"/></component></application>')
   $isolated = $IsolateProjectStores -or $ManagedAssets
   $trustedEntry = if (!$isolated) { '<option value="' + [Security.SecurityElement]::Escape($solutionFile.DirectoryName) + '"/>' }
   [IO.File]::WriteAllText((Join-Path $optionsDir trusted-paths.xml), "<application><component name=""Trusted.Paths.Settings""><option name=""TRUSTED_PATHS""><list>$trustedEntry</list></option></component></application>")
   if ($isolated) { $plan.projectStores = [IO.Directory]::CreateDirectory((Join-Path $Run project-stores)).FullName }
   $settings = [ordered]@{ 'ide.no.platform.update'='true'; 'idea.suppress.statistics.report'='true'; 'shared.indexes.download'='false'
                          'shared.indexes.download.auto.consent'='false'; 'vcs.root.auto.add'='false' }
   foreach ($name in 'config','system','log','plugins') { $settings["idea.$name.path"] = $folders[$name].Replace('\','/') }
   $properties = Join-Path $Run idea.properties
   $settingLines = @($settings.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" })
   [IO.File]::WriteAllLines($properties, [string[]]$settingLines)
   $standardOptions = @(Get-Content -LiteralPath (Join-Path $install $launch.vmOptionsFilePath) | Where-Object { $_.Trim() -and $_ -notmatch '^\s*(#|-Xmx)' })
   $productOptions = @($launch.additionalJvmArguments | ForEach-Object {
      $_.Replace('%IDE_HOME%', $install).Replace('%USER_HOME%', $folders.home).Replace('%IDE_CACHE_DIR%', $folders.system).Replace('\','/')
   })
   $jars = @(foreach ($name in $launch.bootClassPathJarNames) { (Get-Item -LiteralPath (Join-Path $install "lib/$name")).FullName })
   foreach ($command in 'host','exit') {
      $heap = if ($command -eq 'host') { $HeapMiB } else { 256 }
      $vmOptions = Join-Path $Run "$command.vmoptions"
      $vm = $standardOptions + $productOptions + @("-Xmx${heap}m", "-Didea.home.path=$install", "-Didea.properties.file=$properties",
              "-Djb.vmOptionsFile=$vmOptions", "-Duser.home=$($folders.home)", "-Djava.io.tmpdir=$($folders.temp)",
              '-Didea.required.plugins.id=com.jetbrains.remoteDevelopment', '-Dide.started.from.remote.dev.launcher=true',
              "-XX:HeapDumpPath=$Run/$command.hprof", "-XX:ErrorFile=$Run/$command-error-%p.log") +
              @($settingLines | ForEach-Object { "-D$_" })
      [IO.File]::WriteAllLines($vmOptions, [string[]]$vm)
      $arguments = $vm + @('-classpath', ($jars -join ';'), $launch.mainClass)
      if ($command -eq 'host') {
         $arguments += 'remoteDevHost'
         if (!$isolated) { $arguments += $plan.solution }
         $arguments += @('--listenOn','127.0.0.1','--port',[string]$RemoteDevPort,
                         '--ssh-link-host','127.0.0.1','--ssh-link-user',$env:USERNAME,'--ssh-link-port','22')
      } else { $arguments += 'exit' }
      $quoted = @($arguments | ForEach-Object { '"' + $_.Replace('\','\\').Replace('"','\"') + '"' })
      [IO.File]::WriteAllLines((Join-Path $Run "$command.args"), [string[]]$quoted)
   }
   $plan.environment = @{ APPDATA=$folders.roaming; LOCALAPPDATA=$folders.local; TEMP=$folders.temp; TMP=$folders.temp
      JET_LOCAL_APPDATA_DIR=$folders.'resharper-local'; JET_ROAMING_APPDATA_DIR=$folders.'resharper-roaming'; JET_TEMP_DIR=$folders.temp
      RESHARPER_HOST_LOG_DIR=(Join-Path $folders.log dotnet); DOTNET_CLI_HOME=$folders.cli; DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'
      NUGET_PACKAGES=$folders.packages; NUGET_HTTP_CACHE_PATH=$folders.'nuget-http'; NUGET_PLUGINS_CACHE_PATH=$folders.'nuget-plugins'
      RestoreFallbackFolders=(Join-Path $env:USERPROFILE '.nuget/packages'); DOTNET_NOLOGO='1'; DOTNET_CLI_TELEMETRY_OPTOUT='1'
      DOTNET_GENERATE_ASPNET_CERTIFICATE='false'; DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='1'
      JETBRAINS_NO_CHECK_FOR_UPDATES='1'; RIDER_PROPERTIES=$properties; RIDER_VM_OPTIONS=(Join-Path $Run host.vmoptions)
      # MSBuild's reusable worker nodes outlive the build that started them by up to 15 idle minutes. Started by a
      # backend's restore or build, they stay in the host's job, and closing that backend's project waits until they
      # are gone: on 2026-11-24 seven dotnet processes, by all signs such nodes, held up a worktree's reopening, and its
      # calls, for about 40 minutes.
      MSBUILDDISABLENODEREUSE='1'
      DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT=[string]$RoyderPort; DOTNET_ANALYSIS_AND_REFACTORING_MCP_TOOLS=$folders.tools }
   if ($ManagedAssets) { $plan.environment.ROYDER_REMOTE_ROSLYN_WORKING_DIRECTORY = [IO.Directory]::CreateDirectory((Join-Path $Run roslyn)).FullName }
   if ($isolated) { $plan.environment.ROYDER_PRIVATE_PROJECT_STORES = $plan.projectStores }
   return $plan
}
