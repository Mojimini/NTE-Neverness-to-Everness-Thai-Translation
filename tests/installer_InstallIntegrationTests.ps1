$ErrorActionPreference = 'Stop'
$distRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $distRoot 'tools\InnoSetup673\ISCC.exe'
$builder = Join-Path $distRoot 'installer\build.ps1'
$script = Join-Path $distRoot 'installer\Setup.iss'
if (-not (Test-Path -LiteralPath $compiler)) { throw "Inno Setup 6.7.3 compiler missing: $compiler" }
if (-not (Test-Path -LiteralPath $builder)) { throw "build wrapper missing: $builder" }
if (-not (Test-Path -LiteralPath $script)) { throw "installer script missing: $script" }

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Host "PASS $message"
}
function Put([string]$path, [string]$text) {
    $parent = Split-Path -Parent $path
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    [IO.File]::WriteAllText($path, $text, [Text.Encoding]::UTF8)
}
function New-RuntimeFixture([string]$root, [string]$build) {
    Put (Join-Path $root 'เปิดคำแปลรุ่นหลัก-v28c.exe') "launcher-$build"
    Put (Join-Path $root 'AntdUI.dll') 'fixture-ui-library'
    Put (Join-Path $root 'ThaiUpdate.exe') 'fixture-updater'
    Put (Join-Path $root 'release-config.json') '{"repository":"fixture/repo"}'
    Put (Join-Path $root 'content-catalog.json') '{"releaseTag":"fixture"}'
    Put (Join-Path $root 'วิธีใช้.md') 'fixture guide'
    Put (Join-Path $root 'Fonts\Default\current.ttf') 'thai-font'
    Put (Join-Path $root 'Fonts\Default\english.ttf') 'english-font'
    Put (Join-Path $root 'Fonts\Default\default_profile.json') '{"default":true}'
    Put (Join-Path $root 'Fonts\Default\default_manifest.json') '{"baseline":"fixture"}'
    Put (Join-Path $root 'Fonts\Bundled\bundled_fonts.json') '{"fonts":[]}'
    Put (Join-Path $root 'Fonts\Bundled\Monocraft\font.ttf') 'bundled-font'
    Put (Join-Path $root 'Fonts\Bundled\Monocraft\preview.ttf') 'bundled-preview'
    Put (Join-Path $root 'Licenses\THIRD-PARTY.txt') 'fixture license'
    Put (Join-Path $root 'Licenses\Nested\THIRD-PARTY-NESTED.txt') 'fixture nested license'
}
function Compile-TestInstaller([string]$runtime, [string]$output, [string]$version) {
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $compilerArgs = @()
    if ($version -eq '1.0.2') { $compilerArgs = @('-InnoCompiler', $compiler) }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $builder -Version $version -RuntimeDir $runtime -OutputDir $output -TestBuild @compilerArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "installer build wrapper failed for $version (exit $LASTEXITCODE)" }
    $path = Join-Path $output "Thai-Translator-TEST-Setup-$version.exe"
    if (-not (Test-Path -LiteralPath $path)) { throw "ISCC did not create $path" }
    return $path
}
function Run-Setup([string]$path, [string]$appDir, [bool]$update, [string]$gateMarker = '') {
    $args = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $appDir + '"'),('/LOG="' + (Join-Path $root 'setup.log') + '"'))
    if ($update) { $args += '/NTEUPDATE=1' }
    if ($gateMarker) { $args += ('/NTE_TEST_GATE_MARKER="' + $gateMarker + '"') }
    $p = Start-Process -FilePath $path -ArgumentList $args -PassThru -Wait -WindowStyle Hidden
    if ($p.ExitCode -ne 0) {
        if (Test-Path (Join-Path $root 'setup.log')) { Get-Content (Join-Path $root 'setup.log') | Write-Host }
        throw "Setup returned $($p.ExitCode) for $path"
    }
}
function Get-AppGateName([string]$appDir) {
    $normalized = [IO.Path]::GetFullPath($appDir).TrimEnd('\','/').ToLowerInvariant()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hex = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($normalized))).Replace('-','') }
    finally { $sha.Dispose() }
    return 'Local\NTEThaiUi2_' + $hex.Substring(0,16)
}
function Try-CreateJunction([string]$link, [string]$target) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $link), $target | Out-Null
    $cmd = $env:ComSpec
    $process = Start-Process -FilePath $cmd -ArgumentList @('/c','mklink','/J',('"' + $link + '"'),('"' + $target + '"')) -PassThru -Wait -WindowStyle Hidden
    return ($process.ExitCode -eq 0 -and (Test-Path -LiteralPath $link))
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('nte-install-test-' + [Guid]::NewGuid().ToString('N'))
try {
    $runtimeV1 = Join-Path $root 'runtime-v1'
    $runtimeV2 = Join-Path $root 'runtime-v2'
    $outV1 = Join-Path $root 'out-v1'
    $outV2 = Join-Path $root 'out-v2'
    $outProduction = Join-Path $root 'out-production'
    $appDir = Join-Path $root 'chosen แอป directory'
    New-RuntimeFixture $runtimeV1 'v1'
    New-RuntimeFixture $runtimeV2 'v2'
    $setupV1 = Compile-TestInstaller $runtimeV1 $outV1 '1.0.0'
    $setupV2 = Compile-TestInstaller $runtimeV2 $outV2 '1.0.1'
    & $compiler '/DAppVersion=1.0.2' '/DTestBuild=0' "/DRuntimeDir=$runtimeV2" "/DOutputDir=$outProduction" $script | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "production setup compilation failed (exit $LASTEXITCODE)" }
    Assert (Test-Path -LiteralPath (Join-Path $outProduction 'Thai-Translator-Setup-1.0.2.exe')) 'emits the stable production installer filename separately from TEST builds'

    $unsafeApp = Join-Path $root 'unsafe install target'
    $externalFonts = Join-Path $root 'outside target data'
    New-Item -ItemType Directory -Force -Path $unsafeApp, $externalFonts | Out-Null
    if (Try-CreateJunction (Join-Path $unsafeApp 'Fonts') $externalFonts) {
        $unsafeArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $unsafeApp + '"'))
        $unsafeSetup = Start-Process -FilePath $setupV1 -ArgumentList $unsafeArgs -PassThru -Wait -WindowStyle Hidden
        Assert ($unsafeSetup.ExitCode -ne 0) 'rejects an install target containing a reparse directory'
        Assert (-not (Test-Path -LiteralPath (Join-Path $unsafeApp 'เปิดคำแปลรุ่นหลัก-v28c.exe')) -and
            ((Get-ChildItem -LiteralPath $externalFonts -Force | Measure-Object).Count -eq 0)) 'does not write package files through the reparse target'
    }
    else { Write-Host 'SKIP reparse install target test: Windows refused a private fixture junction' }

    $unsafeLicenseApp = Join-Path $root 'unsafe nested license target'
    $externalLicenses = Join-Path $root 'outside license data'
    New-Item -ItemType Directory -Force -Path (Join-Path $unsafeLicenseApp 'Licenses'), $externalLicenses | Out-Null
    if (Try-CreateJunction (Join-Path $unsafeLicenseApp 'Licenses\Nested') $externalLicenses) {
        $unsafeLicenseArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $unsafeLicenseApp + '"'))
        $unsafeLicenseSetup = Start-Process -FilePath $setupV1 -ArgumentList $unsafeLicenseArgs -PassThru -Wait -WindowStyle Hidden
        Assert ($unsafeLicenseSetup.ExitCode -ne 0) 'rejects a nested license destination reparse point'
        Assert (-not (Test-Path -LiteralPath (Join-Path $unsafeLicenseApp 'เปิดคำแปลรุ่นหลัก-v28c.exe')) -and
            ((Get-ChildItem -LiteralPath $externalLicenses -Force | Measure-Object).Count -eq 0)) 'does not write nested license payload outside the selected folder'
    }
    else { Write-Host 'SKIP nested license reparse test: Windows refused a private fixture junction' }

    Run-Setup $setupV1 $appDir $false
    $appExe = Join-Path $appDir 'เปิดคำแปลรุ่นหลัก-v28c.exe'
    Assert ((Get-Content -Raw -LiteralPath $appExe) -match 'launcher-v1') 'installs to the user-chosen directory'
    Assert ((Get-Content -Raw -LiteralPath (Join-Path $appDir 'แพ็ก\manifest.json')) -match 'fixture') 'seeds the selected manifest on first install'
    Assert (Test-Path -LiteralPath (Join-Path $appDir 'unins000.exe')) 'creates a per-user uninstaller'

    Put (Join-Path $appDir 'Settings\user-settings.json') '{"theme":"dark"}'
    Put (Join-Path $appDir 'Settings\Downloads\cached-installer.bin') 'cached'
    Put (Join-Path $appDir 'Fonts\Imported\user-font.ttf') 'custom-font'
    Put (Join-Path $appDir 'Fonts\Profiles\thai.json') '{"font":"user"}'
    Put (Join-Path $appDir 'Fonts\Requests\request.json') '{"request":true}'
    Put (Join-Path $appDir 'Profiles\profile.json') '{"profile":"user"}'
    Put (Join-Path $appDir 'Requests\request.json') '{"request":"user"}'
    Put (Join-Path $appDir 'Fonts\selection.json') '{"id":"thai-custom"}'
    Put (Join-Path $appDir 'Fonts\english_selection.json') '{"id":"english-custom"}'
    Put (Join-Path $appDir 'Fonts\library_thai.json') '{"fonts":["thai-custom"]}'
    Put (Join-Path $appDir 'Fonts\library_english.json') '{"fonts":["english-custom"]}'
    Put (Join-Path $appDir 'ตำแหน่งเกม.txt') 'D:\FixtureGame'
    Put (Join-Path $appDir 'receipt.json') '{"active":true}'
    Put (Join-Path $appDir 'แพ็ก\manifest.json') '{"selected":"user-manifest"}'

    $operationLock = Join-Path $appDir 'operation.lock'
    $lockHandle = [IO.File]::Open($operationLock, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $lockedArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $appDir + '"'),'/NTEUPDATE=1')
        $lockedUpdate = Start-Process -FilePath $setupV2 -ArgumentList $lockedArgs -PassThru -Wait -WindowStyle Hidden
        Assert ($lockedUpdate.ExitCode -ne 0) 'refuses to replace files while operation.lock is held'
        Assert ((Get-Content -Raw -LiteralPath $appExe) -match 'launcher-v1') 'keeps the current app intact when setup is blocked'
    }
    finally {
        $lockHandle.Dispose()
        Remove-Item -LiteralPath $operationLock -Force -ErrorAction SilentlyContinue
    }

    $gateName = Get-AppGateName $appDir
    $createdMutex = $false
    $busyMutex = New-Object System.Threading.Mutex($true, $gateName, [ref]$createdMutex)
    try {
        $busyArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $appDir + '"'),'/NTEUPDATE=1')
        $blockedSetup = Start-Process -FilePath $setupV2 -ArgumentList $busyArgs -PassThru -Wait -WindowStyle Hidden
        Assert ($blockedSetup.ExitCode -ne 0) 'refuses an upgrade while the shared app mutex is held'
        Assert ((Get-Content -Raw -LiteralPath $appExe) -match 'launcher-v1') 'preserves files when the shared mutex blocks setup'
    }
    finally {
        try { $busyMutex.ReleaseMutex() } catch { }
        $busyMutex.Dispose()
    }

    $gateMarker = Join-Path $root 'install-gate-held.ready'
    $setupArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $appDir + '"'),'/NTEUPDATE=1',('/NTE_TEST_GATE_MARKER="' + $gateMarker + '"'))
    $gateSetup = Start-Process -FilePath $setupV2 -ArgumentList $setupArgs -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $gateMarker) -and -not $gateSetup.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 25 }
    if (-not (Test-Path -LiteralPath $gateMarker)) { $gateSetup.WaitForExit(); throw 'setup did not reach the guarded install phase' }
    $probeMutex = New-Object System.Threading.Mutex($false, $gateName)
    $probeAcquired = $false
    try { try { $probeAcquired = $probeMutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $probeAcquired = $true }
        Assert (-not $probeAcquired) 'holds the shared app mutex during the install phase'
    }
    finally {
        if ($probeAcquired) { $probeMutex.ReleaseMutex() }
        $probeMutex.Dispose()
    }
    $gateSetup.WaitForExit()
    if ($gateSetup.ExitCode -ne 0) { throw "mutex-held setup returned $($gateSetup.ExitCode)" }
    Assert ((Get-Content -Raw -LiteralPath $appExe) -match 'launcher-v2') 'updates static runtime files in place'
    foreach ($relative in @('Settings\user-settings.json','Settings\Downloads\cached-installer.bin',
        'Fonts\Imported\user-font.ttf','Fonts\Profiles\thai.json','Fonts\Requests\request.json',
        'Profiles\profile.json','Requests\request.json',
        'Fonts\selection.json','Fonts\english_selection.json','Fonts\library_thai.json',
        'Fonts\library_english.json','ตำแหน่งเกม.txt','receipt.json')) {
        Assert (Test-Path -LiteralPath (Join-Path $appDir $relative)) "preserves $relative during upgrade"
    }
    Assert ((Get-Content -Raw -LiteralPath (Join-Path $appDir 'แพ็ก\manifest.json')) -match 'user-manifest') 'preserves the selected game manifest during upgrade'

    $uninstaller = Join-Path $appDir 'unins000.exe'
    $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -PassThru -Wait -WindowStyle Hidden
    Assert (Test-Path -LiteralPath $appExe) 'refuses uninstall while the active root receipt exists'

    Remove-Item -LiteralPath (Join-Path $appDir 'receipt.json') -Force
    $nestedLicenseLink = Join-Path $appDir 'Licenses\Nested'
    Remove-Item -LiteralPath $nestedLicenseLink -Recurse -Force
    $externalUninstallLicenses = Join-Path $root 'outside uninstall license data'
    if (Try-CreateJunction $nestedLicenseLink $externalUninstallLicenses) {
        $nestedLicenseUninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -PassThru -Wait -WindowStyle Hidden
        Assert (Test-Path -LiteralPath $appExe) 'refuses uninstall when a nested license destination is a reparse point'
        Assert ($nestedLicenseUninstall.ExitCode -ne 0) 'reports a blocked uninstall for a nested license reparse point'
        Remove-Item -LiteralPath $nestedLicenseLink -Force
    }
    else { Write-Host 'SKIP nested license uninstall test: Windows refused a private fixture junction' }

    $receiptRaceMarker = Join-Path $root 'uninstall-receipt-gap.ready'
    $receiptRaceArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/NTE_TEST_UNINSTALL_RECEIPT_GAP_MARKER="' + $receiptRaceMarker + '"'))
    $receiptRace = Start-Process -FilePath $uninstaller -ArgumentList $receiptRaceArgs -PassThru -WindowStyle Hidden
    $receiptDeadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $receiptRaceMarker) -and -not $receiptRace.HasExited -and [DateTime]::UtcNow -lt $receiptDeadline) { Start-Sleep -Milliseconds 25 }
    if (-not (Test-Path -LiteralPath $receiptRaceMarker)) { $receiptRace.WaitForExit(); throw 'uninstaller did not reach the receipt-check gap' }
    Put (Join-Path $appDir 'receipt.json') '{"race":"created after initial check"}'
    $receiptRace.WaitForExit()
    Assert (Test-Path -LiteralPath $appExe) 'rechecks a newly created receipt after acquiring uninstall guards'
    Remove-Item -LiteralPath (Join-Path $appDir 'receipt.json') -Force
    $lockHandle = [IO.File]::Open($operationLock, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $blockedUninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -PassThru -Wait -WindowStyle Hidden
        Assert ($blockedUninstall.ExitCode -ne 0) 'returns a blocked status for uninstall during an operation'
        Assert (Test-Path -LiteralPath $appExe) 'refuses uninstall while an operation lock is held'
    }
    finally {
        $lockHandle.Dispose()
        Remove-Item -LiteralPath $operationLock -Force -ErrorAction SilentlyContinue
    }

    $createdMutex = $false
    $busyMutex = New-Object System.Threading.Mutex($true, $gateName, [ref]$createdMutex)
    try {
        $blockedUninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -PassThru -Wait -WindowStyle Hidden
        Assert ($blockedUninstall.ExitCode -ne 0) 'refuses uninstall while the shared app mutex is held'
        Assert (Test-Path -LiteralPath $appExe) 'preserves files when the shared mutex blocks uninstall'
    }
    finally {
        try { $busyMutex.ReleaseMutex() } catch { }
        $busyMutex.Dispose()
    }

    $uninstallMarker = Join-Path $root 'uninstall-gate-held.ready'
    $uninstallArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/NTE_TEST_UNINSTALL_GATE_MARKER="' + $uninstallMarker + '"'))
    $pendingUninstall = Start-Process -FilePath $uninstaller -ArgumentList $uninstallArgs -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $uninstallMarker) -and -not $pendingUninstall.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 25 }
    if (-not (Test-Path -LiteralPath $uninstallMarker)) { $pendingUninstall.WaitForExit(); throw 'uninstaller did not reach the guarded phase' }
    $probeMutex = New-Object System.Threading.Mutex($false, $gateName)
    $probeAcquired = $false
    try { try { $probeAcquired = $probeMutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $probeAcquired = $true }
        Assert (-not $probeAcquired) 'holds the shared app mutex during uninstall'
    }
    finally {
        if ($probeAcquired) { $probeMutex.ReleaseMutex() }
        $probeMutex.Dispose()
    }
    $fileLockWasFree = $false
    try { $probeFile = [IO.File]::Open($operationLock, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None); $fileLockWasFree = $true; $probeFile.Dispose() }
    catch [IO.IOException] { }
    Assert (-not $fileLockWasFree) 'holds operation.lock exclusively during uninstall'
    $pendingUninstall.WaitForExit()
    Assert (-not (Test-Path -LiteralPath $appExe)) 'uninstalls after the active receipt is removed'
    Assert (Test-Path -LiteralPath (Join-Path $appDir 'Settings\user-settings.json')) 'leaves user Settings data after uninstall'
    Assert (Test-Path -LiteralPath (Join-Path $appDir 'Fonts\Imported\user-font.ttf')) 'leaves imported fonts after uninstall'
    Write-Host 'INSTALLER_INTEGRATION_TESTS_PASS'
}
finally {
    $fullRoot = [IO.Path]::GetFullPath($root).TrimEnd('\')
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $env:KEEP_INSTALL_TEST -and $fullRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $fullRoot)) {
        $removed = $false
        for ($attempt = 0; $attempt -lt 30 -and -not $removed; $attempt++) {
            try {
                Remove-Item -LiteralPath $fullRoot -Recurse -Force -ErrorAction Stop
                $removed = $true
            }
            catch {
                if ($attempt -eq 29) { throw }
                Start-Sleep -Milliseconds 100
            }
        }
    }
}
