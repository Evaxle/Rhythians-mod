param([string]$Zig = 'zig', [string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $Zig cc -shared -O2 (Join-Path $PSScriptRoot 'native/raylib.c') (Join-Path $PSScriptRoot 'native/raylib_ogl.def') -lshell32 -o (Join-Path $output 'raylib_ogl.dll')
if ($LASTEXITCODE) { throw 'Native build failed.' }
& $Dotnet publish (Join-Path $PSScriptRoot 'bridge') -c Release -r win-x64 --self-contained true -p:DebugType=None -o (Join-Path $output 'Rhythians')
if ($LASTEXITCODE) { throw 'Helper build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets') -Destination (Join-Path $output 'Rhythians') -Recurse -Force
foreach ($name in @('Install.ps1','Uninstall.ps1','README.md')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $output -Force }
$payload = Join-Path $PSScriptRoot 'installer/payload.zip'
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($payload, [IO.FileMode]::Create)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    $files = @((Get-Item -LiteralPath (Join-Path $output 'raylib_ogl.dll'))) + @(Get-ChildItem -LiteralPath (Join-Path $output 'Rhythians') -File -Recurse | Where-Object Extension -ne '.pdb')
    foreach ($file in $files) {
        $name = [IO.Path]::GetRelativePath($output, $file.FullName).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose(); $stream.Dispose() }
& $Dotnet publish (Join-Path $PSScriptRoot 'installer') -c Release -r win-x64 --self-contained true -p:DebugType=None -o (Join-Path $output 'installer')
if ($LASTEXITCODE) { throw 'Installer build failed.' }
Copy-Item -LiteralPath (Join-Path $output 'installer/Rhythians.exe') -Destination (Join-Path $output 'Rhythians.exe') -Force
Write-Host "Built $output"
