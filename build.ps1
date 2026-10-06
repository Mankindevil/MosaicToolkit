param([string]$OutputName = 'MosaicToolkit.exe')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $PSScriptRoot $OutputName
$arguments = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/warnaserror+', "/out:$out", "/win32manifest:$PSScriptRoot\app.manifest")
$arguments += @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll', '/reference:System.Runtime.Serialization.dll', '/reference:System.Xml.dll')
$arguments += "/resource:$PSScriptRoot\src\Shared.cs,Toolkit.Shared.cs"
$arguments += "/resource:$PSScriptRoot\src\RuntimePlugin.cs,Toolkit.RuntimePlugin.cs"
$arguments += @('Shared.cs','DesktopServices.cs','Desktop.cs','MaterialDetailsForm.cs','SelfTests.cs') | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Desktop compilation failed' }
Get-FileHash -LiteralPath $out -Algorithm SHA256
