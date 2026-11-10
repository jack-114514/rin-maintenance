param([string]$OutputDirectory=(Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot 'src'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& (Join-Path $source 'make-icon.ps1') -OutputPath (Join-Path $source 'manager.ico')
$arguments=@('/nologo','/target:winexe','/main:RinManager','/utf8output','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:Microsoft.CSharp.dll',('/win32manifest:'+(Join-Path $source 'maintenance.manifest')),('/win32icon:'+(Join-Path $source 'manager.ico')),('/out:'+(Join-Path $OutputDirectory '小凛管家.exe')))
& $compiler @arguments (Join-Path $source 'Maintenance.cs') (Join-Path $source 'Manager.cs') (Join-Path $source 'Theme.cs') (Join-Path $source 'StartupRegistration.cs')
if($LASTEXITCODE){throw 'Build failed'}
Get-Item (Join-Path $OutputDirectory '小凛管家.exe') | Select-Object Name,Length
