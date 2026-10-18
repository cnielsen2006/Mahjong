# Builds the Windows player with Unity (headless) and runs the in-engine autotest.
# Usage: powershell -File tools\build_and_test.ps1 [-SkipBuild] [-SkipTest]
param([switch]$SkipBuild, [switch]$SkipTest)

$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe"
$proj  = Split-Path -Parent $PSScriptRoot
$logs  = Join-Path $proj "build\logs"
New-Item -ItemType Directory -Force $logs | Out-Null

if (-not $SkipBuild) {
    $log = Join-Path $logs "build_win.log"
    $p = Start-Process -FilePath $unity -ArgumentList '-batchmode','-nographics','-quit','-projectPath',$proj,'-executeMethod','Mahjong.EditorTools.BuildScript.BuildWindows','-logFile',$log -PassThru -Wait
    Write-Output "build exit code: $($p.ExitCode)"
    Select-String -Path $log -Pattern "error CS|Build StandaloneWindows64" | ForEach-Object { $_.Line }
    if ($p.ExitCode -ne 0) { exit 1 }
}

if (-not $SkipTest) {
    $exe   = Join-Path $proj "Builds\Windows\Mahjong.exe"
    $shots = Join-Path $proj "build\shots"
    $save  = Join-Path $proj "build\testsave"
    Remove-Item $shots -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $save -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory $shots | Out-Null
    $p = Start-Process -FilePath $exe -ArgumentList '-screen-width','1280','-screen-height','800','-screen-fullscreen','0','-autotest','-shots',$shots,'-savedir',$save,'-logFile',"$shots\player.log" -PassThru
    if (-not $p.WaitForExit(300000)) { $p.Kill(); Write-Output "TIMEOUT - killed" }
    Write-Output "player exit code: $($p.ExitCode)"
    Select-String -Path "$shots\player.log" -Pattern "AUTOTEST|Exception|rror" | Select-Object -First 80 | ForEach-Object { $_.Line }
}
