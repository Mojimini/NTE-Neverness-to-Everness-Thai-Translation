$ErrorActionPreference='Stop'
$dist=Split-Path -Parent $PSScriptRoot
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs=@('/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',"/r:$dist\runtime\AntdUI.dll")
$wpf=Join-Path (Split-Path -Parent $csc) 'WPF'
$refs+=@('/r:System.Xaml.dll',"/r:$wpf\PresentationCore.dll","/r:$wpf\PresentationFramework.dll","/r:$wpf\WindowsBase.dll","/r:$wpf\UIAutomationProvider.dll","/r:$wpf\UIAutomationTypes.dll")
if(!(Test-Path -LiteralPath "$dist\generated\ReleaseVersion.cs")){throw 'Build the application first to generate the version source.'}
$ui=@('src\ToggleUiV2.cs','src\UpdateUi.cs','src\LatestProgressDispatcher.cs','generated\ReleaseVersion.cs','src\DistributionModels.cs','baseline\UiContracts.cs','baseline\ThemeSettings.cs','baseline\WindowChrome.cs','baseline\AnimatedProgressControl.cs') | ForEach-Object{Join-Path $dist $_}
Copy-Item -LiteralPath "$dist\runtime\AntdUI.dll" -Destination $PSScriptRoot -Force
function Run-Test($name,$entry,$sources) {
    $exe=Join-Path $PSScriptRoot "$name.exe"
    & $csc /nologo "/out:$exe" "/main:$entry" $refs $sources
    if($LASTEXITCODE -ne 0){throw "$name compile failed"}
    & $exe 2>&1 | Tee-Object -FilePath (Join-Path $PSScriptRoot "$name.log")
    if($LASTEXITCODE -ne 0){throw "$name failed"}
}
Run-Test 'delivery_tests' 'GitHubDeliveryClientTests' @("$dist\src\GitHubDeliveryClient.cs","$dist\src\DistributionModels.cs","$PSScriptRoot\delivery_GitHubDeliveryClientTests.cs")
Run-Test 'LazyDeliveryTests' 'LazyDeliveryTests' @("$dist\src\DeliveredLauncherService.cs","$dist\src\DistributionModels.cs","$dist\baseline\UiContracts.cs","$PSScriptRoot\LazyDeliveryTests.cs")
Run-Test 'UpdateUiTests' 'UpdateUiTests' ($ui+@("$PSScriptRoot\UpdateUiTests.cs"))
Run-Test 'Ui6RegressionHarness' 'Ui6RegressionHarness' ($ui+@("$PSScriptRoot\Ui6RegressionHarness.cs"))
if(Test-Path -LiteralPath "$PSScriptRoot\WpfUiTests.cs") {
    $retained=@('src\WpfLauncherWindow.cs','src\WpfView.cs','src\RetainedProgressControl.cs','src\LatestProgressDispatcher.cs','generated\ReleaseVersion.cs','src\DistributionModels.cs','baseline\UiContracts.cs','baseline\ThemeSettings.cs','tests\WpfUiTests.cs','tests\WpfTestFixtures.cs') | ForEach-Object{Join-Path $dist $_}
    Run-Test 'WpfUiTests' 'WpfUiTests' $retained
    Run-Test 'ProgressDispatchTests' 'ProgressDispatchTests' @("$dist\src\LatestProgressDispatcher.cs","$dist\baseline\UiContracts.cs","$PSScriptRoot\ProgressDispatchTests.cs")
    Run-Test 'RetainedProgressTests' 'RetainedProgressTests' @("$dist\src\RetainedProgressControl.cs","$dist\baseline\ThemeSettings.cs","$PSScriptRoot\RetainedProgressTests.cs")
}
& $csc /nologo "/out:$PSScriptRoot\HandoffFakeHelper.exe" "$PSScriptRoot\HandoffFakeHelper.cs"
if($LASTEXITCODE -ne 0){throw 'Fake helper compilation failed'}
Run-Test 'HandoffTests' 'HandoffTests' @("$dist\src\UpdateHandoff.cs","$PSScriptRoot\HandoffTests.cs")
& $csc /nologo "/out:$PSScriptRoot\installer_FakeInstaller.exe" "$PSScriptRoot\installer_FakeInstaller.cs"
if($LASTEXITCODE -ne 0){throw 'Fake installer compilation failed'}
& $csc /nologo "/out:$PSScriptRoot\installer_UpdaterHelper.exe" "$dist\src\UpdaterHelper.cs"
if($LASTEXITCODE -ne 0){throw 'Helper compilation failed'}
Run-Test 'installer_UpdaterHelperTests' 'InstallerUpdaterHelperTests' @("$PSScriptRoot\installer_UpdaterHelperTests.cs")
if(Test-Path -LiteralPath "$dist\assets\translation-v28c.zip") {
    $integration=@('src\GitHubDeliveryClient.cs','src\DistributionModels.cs','src\DeliveredLauncherService.cs','baseline\UiContracts.cs','baseline\LauncherService.cs','baseline\FontLibrary.cs','baseline\WorkerJob.cs','baseline\ToggleCore.cs','tests\IntegrationDeliveryTests.cs') | ForEach-Object{Join-Path $dist $_}
    Run-Test 'IntegrationDeliveryTests' 'IntegrationDeliveryTests' $integration
}
