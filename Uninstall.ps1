param([string]$GameFolder = 'C:\Program Files (x86)\Steam\steamapps\common\rhythia')
$ErrorActionPreference = 'Stop'
if (Get-Process rhythia -ErrorAction SilentlyContinue) { throw 'Close Rhythia before uninstalling.' }
$gameRoot = (Resolve-Path -LiteralPath $GameFolder).Path
$backup = Join-Path $gameRoot 'RhythiansRaylib.dll'
if ((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne 'FBD590391E9BA9CB73C147018AD39CF003132E3C788D1EDAA8E55B946642EC05') { throw 'The original library backup does not match.' }
Get-Process Rhythians.Bridge -ErrorAction SilentlyContinue | Stop-Process
Copy-Item -LiteralPath $backup -Destination (Join-Path $gameRoot 'raylib_ogl.dll') -Force
Write-Host 'Restored Rhythia. Saved login and pending scores remain in your local RhythiansMod folder.'
