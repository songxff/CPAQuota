param([string]$OutputDirectory=(Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference='Stop'
$framework=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$outputFile=Join-Path $OutputDirectory 'CPA状态栏.exe'
$refs=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Security.dll')
$arguments=@('/nologo','/target:winexe','/optimize+','/platform:x86','/utf8output',('/out:'+$outputFile),('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')))
foreach($reference in $refs){$arguments+=('/reference:'+(Join-Path $framework $reference))}
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'app.ico')){$arguments+=('/win32icon:'+(Join-Path $PSScriptRoot 'app.ico'))}
$arguments+=(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs'|ForEach-Object FullName)
& $compiler $arguments
if($LASTEXITCODE -ne 0){throw 'EXE compilation failed.'}
Write-Host ('Built: '+$outputFile)
