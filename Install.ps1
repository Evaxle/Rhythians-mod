param([string]$GameFolder = 'C:\Program Files (x86)\Steam\steamapps\common\rhythia')
$ErrorActionPreference = 'Stop'
if (Get-Process rhythia -ErrorAction SilentlyContinue) { throw 'Close Rhythia before installing.' }
$gameRoot = (Resolve-Path -LiteralPath $GameFolder).Path
$gameExe = Join-Path $gameRoot 'rhythia.exe'
if ((Get-FileHash -LiteralPath $gameExe -Algorithm SHA256).Hash -ne '857C0D71B8CBD0C3F07F9FCD60E007680D5CDE6522C2904FC4DE4D937BEEBD1A') { throw 'This Rhythia build is not supported yet.' }
$library = Join-Path $gameRoot 'raylib_ogl.dll'
$backup = Join-Path $gameRoot 'RhythiansRaylib.dll'
$originalHash = 'FBD590391E9BA9CB73C147018AD39CF003132E3C788D1EDAA8E55B946642EC05'
if (Test-Path -LiteralPath $backup) {
    if ((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne $originalHash) { throw 'The original library backup does not match.' }
} else {
    if ((Get-FileHash -LiteralPath $library -Algorithm SHA256).Hash -ne $originalHash) { throw 'The graphics library is already modified.' }
    Copy-Item -LiteralPath $library -Destination $backup
}
Get-Process Rhythians.Bridge -ErrorAction SilentlyContinue | Stop-Process
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Rhythians') -Destination $gameRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'raylib_ogl.dll') -Destination $library -Force
Write-Host 'Installed. Start Rhythia and click Log in or press F8.'
