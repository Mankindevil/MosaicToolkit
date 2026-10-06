param([string]$ExecutableName = 'MosaicToolkit.exe')
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot $ExecutableName
$p = Start-Process -FilePath $exe -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'test-output\desktop-tests.txt')
if ($p.ExitCode -ne 0) { throw 'Desktop tests failed' }
$uiProcess = Start-Process -FilePath $exe -ArgumentList '--ui-test' -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'test-output\ui-tests.txt')
if ($uiProcess.ExitCode -ne 0) { throw 'WinForms refresh tests failed' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe = Join-Path $PSScriptRoot 'test-output\RuntimeTests.exe'
& $compiler /nologo /target:exe /warnaserror+ "/out:$testExe" /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Runtime.Serialization.dll /reference:System.Xml.dll (Join-Path $PSScriptRoot 'src\Shared.cs') (Join-Path $PSScriptRoot 'src\DesktopServices.cs') (Join-Path $PSScriptRoot 'src\RuntimePlugin.cs') (Join-Path $PSScriptRoot 'tests\UnityStubs.cs') (Join-Path $PSScriptRoot 'tests\RuntimeTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Runtime simulation compilation failed' }
& $testExe | Tee-Object -FilePath (Join-Path $PSScriptRoot 'test-output\runtime-tests.txt')
if ($LASTEXITCODE -ne 0) { throw 'Runtime simulation failed' }
