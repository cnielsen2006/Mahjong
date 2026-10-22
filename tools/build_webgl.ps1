# Builds the WebGL player (Builds/WebGL) with Unity headless.
# Usage: powershell -File tools\build_webgl.ps1
$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe"
$proj  = Split-Path -Parent $PSScriptRoot
$logs  = Join-Path $proj "build\logs"
New-Item -ItemType Directory -Force $logs | Out-Null
$log = Join-Path $logs "build_webgl.log"
$p = Start-Process -FilePath $unity -ArgumentList '-batchmode','-nographics','-quit','-projectPath',$proj,'-buildTarget','WebGL','-executeMethod','Mahjong.EditorTools.BuildScript.BuildWebGL','-logFile',$log -PassThru -Wait
Write-Output "build exit code: $($p.ExitCode)"
Select-String -Path $log -Pattern "error CS|Build WebGL|BuildFailed|Exception|FAILURE|error:" | Select-Object -First 30 | ForEach-Object { $_.Line }
if ($p.ExitCode -ne 0) { exit 1 }
