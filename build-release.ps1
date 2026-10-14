[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [string]$OutputDir = '',
    [string]$InnoCompiler = '',
    [switch]$TestBuild
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*))?$') { throw 'Use a numeric version x.y.z[.w].' }
$distRoot = $PSScriptRoot
if (!$OutputDir) { $OutputDir = Join-Path $distRoot 'release' }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
$runtimeDir = Join-Path $distRoot 'runtime'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw '.NET Framework 4.8 C# compiler is required.' }
$version4 = ([Version]$Version).ToString()
if (($Version.Split('.')).Count -eq 3) { $version4 += '.0' }
$generatedDir = Join-Path $distRoot 'generated'
New-Item -ItemType Directory -Force -Path $generatedDir,$OutputDir | Out-Null
[IO.File]::WriteAllText((Join-Path $generatedDir 'ReleaseVersion.cs'), ('internal static class ReleaseVersion { internal const string Current = "' + $Version + '"; }'), [Text.Encoding]::UTF8)
$assembly = @"
using System.Reflection;
[assembly: AssemblyTitle("NTE Thai Translator")]
[assembly: AssemblyProduct("NTE Thai Translator")]
[assembly: AssemblyCompany("Mojimini")]
[assembly: AssemblyVersion("$version4")]
[assembly: AssemblyFileVersion("$version4")]
[assembly: AssemblyInformationalVersion("$Version")]
"@
[IO.File]::WriteAllText((Join-Path $generatedDir 'AssemblyInfo.cs'),$assembly,[Text.Encoding]::UTF8)
$configFile = Join-Path $runtimeDir 'release-config.json'
$config = Get-Content -LiteralPath $configFile -Raw -Encoding UTF8 | ConvertFrom-Json
$config.CurrentVersion = $Version
[IO.File]::WriteAllText($configFile,($config | ConvertTo-Json),[Text.Encoding]::UTF8)
$sourceFiles = @(
    'src\Program.cs','src\DistributionModels.cs','src\GitHubDeliveryClient.cs','src\DeliveredLauncherService.cs',
    'src\ToggleUiV2.cs','src\UpdateUi.cs','src\UpdateHandoff.cs',
    'baseline\UiContracts.cs','baseline\ThemeSettings.cs','baseline\WindowChrome.cs',
    'baseline\AnimatedProgressControl.cs','baseline\LauncherService.cs','baseline\FontLibrary.cs','baseline\WorkerJob.cs',
    'baseline\ToggleCore.cs','generated\ReleaseVersion.cs','generated\AssemblyInfo.cs'
) | ForEach-Object { Join-Path $distRoot $_ }
$exe = Join-Path $runtimeDir 'เปิดคำแปลรุ่นหลัก-v28c.exe'
& $csc /nologo /target:winexe /platform:anycpu /main:Program "/out:$exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/r:$runtimeDir\AntdUI.dll" $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
& (Join-Path $distRoot 'installer\build.ps1') -Version $Version -RuntimeDir $runtimeDir -OutputDir $OutputDir -TestBuild:$TestBuild -InnoCompiler $InnoCompiler
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$prefix = if ($TestBuild) { 'Thai-Translator-TEST-Setup-' } else { 'Thai-Translator-Setup-' }
$installerPath = Join-Path $OutputDir ($prefix + $Version + '.exe')
$files = @($installerPath)
$assetsDir = Join-Path $distRoot 'assets'
if (Test-Path -LiteralPath $assetsDir) {
    foreach ($file in Get-ChildItem -LiteralPath $assetsDir -File -Filter '*.zip') {
        $destination = Join-Path $OutputDir $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        $files += $destination
    }
}
$checksums = $files | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_) }
[IO.File]::WriteAllLines((Join-Path $OutputDir 'SHA256SUMS.txt'),[string[]]$checksums,[Text.Encoding]::UTF8)
Write-Output "Built $Version; verified hashes: $OutputDir\SHA256SUMS.txt"
