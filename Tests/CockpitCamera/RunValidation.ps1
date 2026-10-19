param([Parameter(Mandatory=$true)][string]$UnityPath)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validation = Join-Path $repository '.utmp/CockpitCameraValidation'
foreach ($folder in @('Assets/Editor','Assets/UI','Assets/Localization','Assets/Camera','Assets/Resources/Localization','Packages','ProjectSettings','Logs')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $validation $folder) | Out-Null
}
foreach ($source in Get-ChildItem (Join-Path $repository 'Assets/Scripts/UI') -Filter '*.cs') {
    if ($source.Name -ne 'RepairScreenController.cs') { Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $validation 'Assets/UI') -Force }
}
foreach ($source in Get-ChildItem (Join-Path $repository 'Assets/Scripts/Localization') -Filter '*.cs') {
    if ($source.Name -ne 'TerminalDialogueLocalizer.cs') { Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $validation 'Assets/Localization') -Force }
}
foreach ($source in @('LandingCockpitLook.cs','CockpitCameraController.cs','CockpitScreenTarget.cs')) {
    Copy-Item -LiteralPath (Join-Path $repository ('Assets/Landing/Input/' + $source)) -Destination (Join-Path $validation 'Assets/Camera') -Force
}
Copy-Item -LiteralPath (Join-Path $repository 'Assets/InputSystem_Actions.inputactions') -Destination (Join-Path $validation 'Assets') -Force
Copy-Item -LiteralPath (Join-Path $repository 'Assets/Resources/Localization/pioneer60_dict.json') -Destination (Join-Path $validation 'Assets/Resources/Localization') -Force
Copy-Item -LiteralPath (Join-Path $repository 'Assets/Landing/Editor/CockpitCameraInstaller.cs') -Destination (Join-Path $validation 'Assets/Editor') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CockpitCameraValidation.cs') -Destination (Join-Path $validation 'Assets/Editor') -Force
Copy-Item -LiteralPath (Join-Path $repository 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $validation 'ProjectSettings') -Force
$dependencies = (Get-Content (Join-Path $repository 'Packages/manifest.json') -Raw | ConvertFrom-Json).dependencies
$manifest = @{ dependencies = @{'com.unity.ugui'=$dependencies.'com.unity.ugui'; 'com.unity.inputsystem'=$dependencies.'com.unity.inputsystem'; 'com.unity.modules.physics'='1.0.0'; 'com.unity.modules.jsonserialize'='1.0.0'; 'com.unity.modules.imgui'='1.0.0'; 'com.unity.modules.audio'='1.0.0'} } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $validation 'Packages/manifest.json'),$manifest,(New-Object Text.UTF8Encoding($false)))
$log = Join-Path $repository 'Logs/CockpitCameraValidation.log'
$arguments = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod CockpitCameraValidation.Run -logFile "{1}"' -f $validation,$log
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output ('Camera validation PID: {0}. Log: {1}' -f $process.Id,$log)
