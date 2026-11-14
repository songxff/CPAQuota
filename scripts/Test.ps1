param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'Build.ps1')}
$folder=Join-Path $root 'artifacts\tests'
New-Item -ItemType Directory -Path $folder -Force|Out-Null
$report=Join-Path $folder 'test-report.json'
& (Join-Path $root 'artifacts\build\CPAQuota.Tests.exe') --report $report
if($LASTEXITCODE -ne 0){throw 'Tests failed.'}
$r=Get-Content -LiteralPath $report -Raw -Encoding UTF8|ConvertFrom-Json
if(-not $r.passed){throw $r.error}
Write-Host ('Passed '+$r.count+' tests.')
