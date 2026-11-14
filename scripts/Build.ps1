param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $OutputDirectory){$OutputDirectory=Join-Path $root 'artifacts\build'}
$framework=Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
$app=Join-Path $root 'src\CPAQuota'
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$refs=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Security.dll')
$common=@('/nologo','/optimize+','/platform:x86','/utf8output')
foreach($reference in $refs){$common+=('/reference:'+(Join-Path $framework $reference))}
$exe=Join-Path $OutputDirectory 'CPAQuota.exe'
$arguments=$common+@('/target:winexe',('/out:'+$exe),('/win32manifest:'+(Join-Path $app 'Assets\app.manifest')),('/win32icon:'+(Join-Path $app 'Assets\app.ico')))
$arguments+=@(Get-ChildItem -LiteralPath $app -Filter '*.cs'|ForEach-Object FullName)
& $compiler $arguments
if($LASTEXITCODE -ne 0){throw 'Application compilation failed.'}
$testArguments=$common+@('/target:exe',('/out:'+(Join-Path $OutputDirectory 'CPAQuota.Tests.exe')),('/reference:'+$exe))
$testArguments+=@(Get-ChildItem -LiteralPath (Join-Path $root 'tests\CPAQuota.Tests') -Filter '*.cs'|ForEach-Object FullName)
& $compiler $testArguments
if($LASTEXITCODE -ne 0){throw 'Test runner compilation failed.'}
Write-Host ('Built: '+$exe)
