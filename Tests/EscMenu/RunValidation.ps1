param([Parameter(Mandatory=$true)][string]$UnityPath)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validation = Join-Path $repository '.utmp/EscMenuValidation'
New-Item -ItemType Directory -Force -Path (Join-Path $validation 'Assets/Editor'),(Join-Path $validation 'Packages'),(Join-Path $validation 'ProjectSettings'),(Join-Path $repository 'Logs') | Out-Null
foreach ($directory in @('Assets/Scripts/UI','Assets/Scripts/Localization','Assets/Resources/Localization','Assets/Fonts/Chinese','Assets/Fonts/Terminal','Assets/TextMesh Pro','Assets/ArtAssets/UI/ui1111_components')) {
    $destination = Join-Path $validation $directory
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository $directory) -Destination (Split-Path -Parent $destination) -Recurse -Force
}
# Keep the standalone ESC test independent of the repair screen's Xray/URP integration.
foreach ($relative in @('Assets/Scripts/Localization/TerminalDialogueLocalizer.cs','Assets/Scripts/Localization/TerminalDialogueLocalizer.cs.meta','Assets/Scripts/UI/Editor/__UIManagerLiveSetup.cs','Assets/Scripts/UI/Editor/__UIManagerLiveSetup.cs.meta','Assets/Scripts/UI/Editor/__EscMenuLiveSetup.cs','Assets/Scripts/UI/Editor/__EscMenuLiveSetup.cs.meta','Assets/Scripts/UI/RepairScreenController.cs','Assets/Scripts/UI/RepairScreenController.cs.meta','Assets/Scripts/UI/Editor/RepairScreenEditorPreview.cs','Assets/Scripts/UI/Editor/RepairScreenEditorPreview.cs.meta')) {
    $temporarySource = [IO.Path]::GetFullPath((Join-Path $validation $relative))
    if (-not $temporarySource.StartsWith($validation + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected validation file path' }
    if (Test-Path -LiteralPath $temporarySource) { Remove-Item -LiteralPath $temporarySource }
}
foreach ($relative in @('Assets/Prefabs/UI/ESC Menu.prefab','Assets/Prefabs/UI/ESC Menu.prefab.meta','Assets/InputSystem_Actions.inputactions','Assets/InputSystem_Actions.inputactions.meta','ProjectSettings/ProjectVersion.txt','ProjectSettings/ProjectSettings.asset')) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent (Join-Path $validation $relative)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository $relative) -Destination (Join-Path $validation $relative) -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EscMenuValidation.cs') -Destination (Join-Path $validation 'Assets/Editor/EscMenuValidation.cs') -Force
$sourceDependencies = (Get-Content (Join-Path $repository 'Packages/manifest.json') -Raw | ConvertFrom-Json).dependencies
$dependencies = @{}
foreach ($package in @('com.unity.ugui','com.unity.inputsystem')) { $dependencies[$package] = $sourceDependencies.$package }
foreach ($module in @('physics','imgui','animation','imageconversion','audio','jsonserialize')) { $dependencies['com.unity.modules.'+$module] = '1.0.0' }
$manifest = @{ dependencies = $dependencies } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $validation 'Packages/manifest.json'), $manifest, (New-Object Text.UTF8Encoding($false)))
$validationLog = Join-Path $repository 'Logs/EscMenuValidation.log'
$arguments = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod EscMenuValidation.Run -logFile "{1}"' -f $validation,$validationLog
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output ('ESC menu validation process: {0}. Log: {1}' -f $process.Id,$validationLog)
