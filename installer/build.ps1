[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [string]$RuntimeDir = '',
    [string]$OutputDir = '',
    [string]$InnoCompiler = '',
    [switch]$TestBuild
)

$ErrorActionPreference = 'Stop'
$distRoot = Split-Path -Parent $PSScriptRoot
if ([String]::IsNullOrWhiteSpace($RuntimeDir)) { $RuntimeDir = Join-Path $distRoot 'runtime' }
if ([String]::IsNullOrWhiteSpace($OutputDir)) { $OutputDir = Join-Path $PSScriptRoot 'out' }
$setupScript = Join-Path $PSScriptRoot 'Setup.iss'
$bundledCompiler = Join-Path $distRoot 'tools\InnoSetup673\ISCC.exe'
$installedCompiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
$compiler = $InnoCompiler
if ([String]::IsNullOrWhiteSpace($compiler)) {
    if (Test-Path -LiteralPath $bundledCompiler -PathType Leaf) { $compiler = $bundledCompiler }
    elseif (Test-Path -LiteralPath $installedCompiler -PathType Leaf) { $compiler = $installedCompiler }
}
elseif (-not [IO.Path]::IsPathRooted($compiler)) {
    throw 'InnoCompiler must be an absolute path.'
}
$helperSource = Join-Path $distRoot 'src\UpdaterHelper.cs'
$frameworkCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $frameworkCompiler)) {
    $frameworkCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}

if ([String]::IsNullOrWhiteSpace($compiler)) {
    throw 'Inno Setup compiler not found. Install Inno Setup 6.7.3 or newer, or pass -InnoCompiler with its absolute ISCC.exe path.'
}
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Version must be numeric x.y.z[.w], got '$Version'." }
foreach ($path in @($setupScript, $compiler, $helperSource)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required build input missing: $path" }
}
$sig = Get-AuthenticodeSignature -LiteralPath $compiler
if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') {
    throw "ISCC.exe signature is not valid from Pyrsys B.V. (status: $($sig.Status))."
}

$runtimeFull = [IO.Path]::GetFullPath($RuntimeDir)
$outputFull = [IO.Path]::GetFullPath($OutputDir)
$required = @(
    'เปิดคำแปลรุ่นหลัก-v28c.exe', 'AntdUI.dll', 'release-config.json', 'content-catalog.json', 'วิธีใช้.md',
    'Fonts\Default\current.ttf', 'Fonts\Default\english.ttf',
    'Fonts\Default\default_profile.json', 'Fonts\Default\default_manifest.json',
    'Fonts\Bundled\bundled_fonts.json', 'Fonts\Bundled\Monocraft\font.ttf',
    'Fonts\Bundled\Monocraft\preview.ttf'
)
foreach ($relative in $required) {
    $path = Join-Path $runtimeFull $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Runtime allowlist file missing: $path" }
}
if (-not (Test-Path -LiteralPath (Join-Path $runtimeFull 'Licenses') -PathType Container)) {
    throw 'Runtime allowlist directory missing: Licenses.'
}
$releaseConfigPath = Join-Path $runtimeFull 'release-config.json'
$releaseConfig = Get-Content -Raw -Encoding UTF8 -LiteralPath $releaseConfigPath | ConvertFrom-Json
if (-not $TestBuild) {
    if ([string]$releaseConfig.CurrentVersion -ne $Version) {
        throw "Runtime release-config CurrentVersion '$($releaseConfig.CurrentVersion)' does not match build version '$Version'."
    }
    $runtimeVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $runtimeFull 'เปิดคำแปลรุ่นหลัก-v28c.exe')).FileVersion
    $expectedVersion = [Version]::Parse($(if (($Version.Split('.')).Count -eq 3) { "$Version.0" } else { $Version }))
    $actualVersion = $null
    if (-not [Version]::TryParse($runtimeVersion, [ref]$actualVersion) -or $actualVersion -ne $expectedVersion) {
        throw "Runtime launcher FileVersion '$runtimeVersion' does not match build version '$expectedVersion'."
    }
}

New-Item -ItemType Directory -Force -Path $runtimeFull, $outputFull | Out-Null
$helperOut = Join-Path $runtimeFull 'ThaiUpdate.exe'
& $frameworkCompiler /nologo /target:exe "/out:$helperOut" /r:System.dll /r:System.Core.dll /r:System.Security.dll $helperSource
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $helperOut -PathType Leaf)) {
    throw "Failed to compile updater helper into runtime (exit $LASTEXITCODE)."
}
$helperHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $helperOut).Hash.ToLowerInvariant()
if ($null -eq $releaseConfig.PSObject.Properties['UpdaterSha256']) {
    $releaseConfig | Add-Member -NotePropertyName UpdaterSha256 -NotePropertyValue $helperHash
}
else { $releaseConfig.UpdaterSha256 = $helperHash }
$updatedConfigText = ConvertTo-Json -InputObject $releaseConfig -Depth 100
[IO.File]::WriteAllText($releaseConfigPath, $updatedConfigText + "`n", (New-Object Text.UTF8Encoding($false)))

$testFlag = if ($TestBuild) { '1' } else { '0' }
& $compiler "/DAppVersion=$Version" "/DTestBuild=$testFlag" "/DRuntimeDir=$runtimeFull" "/DOutputDir=$outputFull" $setupScript
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)." }
$prefix = if ($TestBuild) { 'Thai-Translator-TEST-Setup-' } else { 'Thai-Translator-Setup-' }
$installer = Join-Path $outputFull ($prefix + $Version + '.exe')
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw "ISCC did not create expected output: $installer" }
Write-Output $installer
