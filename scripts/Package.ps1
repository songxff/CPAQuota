$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Test.ps1')
$exe=Join-Path $root 'artifacts\build\CPAQuota.exe'
$version=((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -split '\.')[0..2] -join '.'
$release=Join-Path $root 'artifacts\releases'
$stage=Join-Path $root ('artifacts\package\'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $release,$stage -Force|Out-Null
Copy-Item -LiteralPath $exe -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'installer\portable-readme.txt') -Destination (Join-Path $stage 'README.txt')
[IO.File]::WriteAllText((Join-Path $stage 'portable.flag'),"portable",[Text.Encoding]::ASCII)
$zip=Join-Path $release ('CPAQuota-'+$version+'-Portable-x86.zip')
Compress-Archive -LiteralPath @((Join-Path $stage 'CPAQuota.exe'),(Join-Path $stage 'README.txt'),(Join-Path $stage 'portable.flag')) -DestinationPath $zip -CompressionLevel Optimal -Force
$msi=Join-Path $release ('CPAQuota-'+$version+'-Setup-x86.msi')
$wixArgs=@('build',(Join-Path $root 'installer\CPAQuota.wxs'),'-arch','x86','-ext','WixToolset.UI.wixext','-culture','zh-CN','-d',('AppExe='+$exe),'-d',('AppIcon='+(Join-Path $root 'src\CPAQuota\Assets\app.ico')),'-d',('Readme='+(Join-Path $root 'installer\installed-readme.txt')),'-d',('Version='+$version),'-pdbtype','none','-o',$msi)
& wix extension add WixToolset.UI.wixext/5.0.2
if($LASTEXITCODE -ne 0){throw 'WiX UI extension setup failed.'}
& wix $wixArgs
if($LASTEXITCODE -ne 0){throw 'Installer build failed.'}
$lines=@($msi,$zip)|ForEach-Object {((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+[IO.Path]::GetFileName($_)}
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'),$lines,(New-Object Text.UTF8Encoding($false)))
Write-Host ('Release packages: '+$release)
