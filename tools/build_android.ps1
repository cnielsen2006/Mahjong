# Builds the Android APK (ARM64 + x86_64) with Unity headless.
# Usage: powershell -File tools\build_android.ps1
$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe"
$proj  = Split-Path -Parent $PSScriptRoot
$logs  = Join-Path $proj "build\logs"
New-Item -ItemType Directory -Force $logs | Out-Null
$log = Join-Path $logs "build_android.log"
$p = Start-Process -FilePath $unity -ArgumentList '-batchmode','-nographics','-quit','-projectPath',$proj,'-buildTarget','Android','-executeMethod','Mahjong.EditorTools.BuildScript.BuildAndroid','-logFile',$log -PassThru -Wait
Write-Output "build exit code: $($p.ExitCode)"
Select-String -Path $log -Pattern "error CS|Build Android|BuildFailed|Exception|FAILURE|error:" | Select-Object -First 30 | ForEach-Object { $_.Line }
if ($p.ExitCode -ne 0) { exit 1 }
