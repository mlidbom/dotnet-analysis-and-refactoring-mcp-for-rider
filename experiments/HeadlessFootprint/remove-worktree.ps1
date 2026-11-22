param([Parameter(Mandatory)][string]$Tree)
$ErrorActionPreference = 'Stop'
$base = [IO.Path]::GetFullPath($PSScriptRoot)
$expectedParent = [IO.Path]::GetFullPath((Join-Path $base 'worktrees'))
$target = (Resolve-Path -LiteralPath $Tree).Path
if ((Split-Path $target -Parent) -ne $expectedParent) { throw "Outside private worktrees: $target" }
$repo = (Resolve-Path -LiteralPath (Join-Path $base 'fixture-repository')).Path
$actual = (& git -C $target rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -or [IO.Path]::GetFullPath($actual) -ne $target) { throw "Unexpected worktree root." }
"ARMED"
if ([Console]::ReadLine() -ne 'go') { exit 2 }
& git -C $repo worktree remove $target
exit $LASTEXITCODE
