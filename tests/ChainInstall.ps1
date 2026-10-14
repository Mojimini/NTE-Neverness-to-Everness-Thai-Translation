$ErrorActionPreference='Stop'
$dist=Split-Path -Parent $PSScriptRoot
$fixtureRoot=Join-Path ([IO.Path]::GetTempPath()) ('nte-chain-'+[Guid]::NewGuid().ToString('N'))
$app=Join-Path $fixtureRoot 'เลือกตำแหน่ง โปรแกรม'
$runtime=Join-Path $dist 'runtime'
$setupOut=Join-Path $fixtureRoot 'setup'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
function Check($condition,$message){if(!$condition){throw $message};Write-Output "PASS $message"}
function Hash($path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
try {
    New-Item -ItemType Directory -Force -Path $app,(Join-Path $app 'Settings\Downloads'),(Join-Path $app 'Fonts\Imported'),(Join-Path $app 'แพ็ก') | Out-Null
    & "$dist\installer\build.ps1" -Version 1.0.0 -RuntimeDir $runtime -OutputDir $setupOut -TestBuild | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Chain test setup build failed'}
    Copy-Item -LiteralPath "$runtime\ThaiUpdate.exe" -Destination $app
    Copy-Item -LiteralPath "$setupOut\Thai-Translator-TEST-Setup-1.0.0.exe" -Destination (Join-Path $app 'Settings\Downloads\next.exe')
    [IO.File]::WriteAllText((Join-Path $app 'receipt.json'),'isolated-active-receipt')
    [IO.File]::WriteAllText((Join-Path $app 'แพ็ก\manifest.json'),'isolated-selected-manifest')
    [IO.File]::WriteAllText((Join-Path $app 'Fonts\Imported\mine.ttf'),'isolated-font')
    [IO.File]::WriteAllText((Join-Path $app 'Settings\user-settings.json'),'isolated-theme')
    $personal=@('receipt.json','แพ็ก\manifest.json','Fonts\Imported\mine.ttf','Settings\user-settings.json')
    $before=@{};foreach($p in $personal){$before[$p]=Hash (Join-Path $app $p)}
    $exe=Join-Path $app 'เปิดคำแปลรุ่นหลัก-v28c.exe'
    & $csc /nologo "/out:$exe" "$PSScriptRoot\ChainParent.cs" "$dist\src\UpdateHandoff.cs"
    if($LASTEXITCODE -ne 0){throw 'Parent compile failed'}
    $parent=Start-Process -FilePath $exe -PassThru -Wait -WindowStyle Hidden
    Check ($parent.ExitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $app 'parent-acknowledged'))) 'actual handoff receives verified helper acknowledgement before parent exits'
    $until=[DateTime]::UtcNow.AddSeconds(20)
    $expected=Hash "$runtime\เปิดคำแปลรุ่นหลัก-v28c.exe"
    while([DateTime]::UtcNow -lt $until){if((Hash $exe) -eq $expected -and (Test-Path -LiteralPath (Join-Path $app 'unins000.exe'))){break};Start-Sleep -Milliseconds 100}
    Check ((Hash $exe) -eq $expected) 'real helper waits for parent then installs the actual application to the chosen Unicode path'
    foreach($p in $personal){Check ((Hash (Join-Path $app $p)) -eq $before[$p]) "actual setup preserves $p after helper handoff"}
    $cfg=Get-Content -LiteralPath (Join-Path $app 'release-config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Check ($cfg.UpdaterSha256 -eq (Hash (Join-Path $app 'ThaiUpdate.exe')).ToLowerInvariant()) 'installed helper matches the SHA-256 pin in installed configuration'
    Check ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -eq '1.0.0.0') 'installed assembly version matches the setup version'
    Start-Sleep -Milliseconds 500
    Remove-Item -LiteralPath (Join-Path $app 'receipt.json')
    $uninstall=Start-Process -FilePath (Join-Path $app 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -PassThru -Wait -WindowStyle Hidden
    Check ($uninstall.ExitCode -eq 0) 'isolated test installation uninstalls after fixture receipt is removed'
    Write-Output 'CHAIN_INSTALL_PASS 9'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixtureRoot)
    $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if(!$resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected cleanup path'}
    for($i=0;$i -lt 30;$i++){try{if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force};break}catch{if($i -eq 29){throw};Start-Sleep -Milliseconds 100}}
}
