param([Parameter(Mandatory = $true)][string]$UnityPath)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validation = Join-Path $repository '.utmp/TerminalPrefabValidation'
New-Item -ItemType Directory -Force -Path (Join-Path $validation 'Assets/Editor'),(Join-Path $validation 'Packages'),(Join-Path $validation 'ProjectSettings'),(Join-Path $repository 'Logs') | Out-Null
foreach ($directory in @('Assets/Scripts/TerminalDialogue','Assets/Fonts/Terminal','Assets/TextMesh Pro','Assets/Dialogue','Assets/ArtAssets/Shaders','Assets/CompactCockpit_WallIntegrated/Materials','Assets/CompactCockpit_WallIntegrated/Textures')) {
    $destination = Join-Path $validation $directory
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository $directory) -Destination (Split-Path -Parent $destination) -Recurse -Force
}
# The optional salvage bridge is unrelated to the standalone terminal prefab under test.
foreach ($relative in @('Assets/Scripts/TerminalDialogue/TerminalSalvageBridge.cs','Assets/Scripts/TerminalDialogue/TerminalSalvageBridge.cs.meta')) {
    $excluded = [IO.Path]::GetFullPath((Join-Path $validation $relative))
    if (-not $excluded.StartsWith($validation + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected validation path' }
    if (Test-Path -LiteralPath $excluded) { Remove-Item -LiteralPath $excluded }
}
foreach ($relative in @('Assets/Scenes/TerminalDialogueDemo.unity','Assets/Scenes/TerminalDialogueDemo.unity.meta','Assets/ArtAssets/3DModels/生命装置.glb','Assets/ArtAssets/3DModels/生命装置.glb.meta','Assets/CompactCockpit_WallIntegrated/Models/Screen_NAVIGATION.glb','Assets/CompactCockpit_WallIntegrated/Models/Screen_NAVIGATION.glb.meta','Assets/CompactCockpit_WallIntegrated/Prefabs/Screens/Screen_NAVIGATION.prefab','Assets/CompactCockpit_WallIntegrated/Prefabs/Screens/Screen_NAVIGATION.prefab.meta')) {
    $destination = Join-Path $validation $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository $relative) -Destination $destination -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PrefabValidation.cs') -Destination (Join-Path $validation 'Assets/Editor/PrefabValidation.cs') -Force
Copy-Item -LiteralPath (Join-Path $repository 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $validation 'ProjectSettings/ProjectVersion.txt') -Force
$projectDependencies = (Get-Content -LiteralPath (Join-Path $repository 'Packages/manifest.json') -Raw | ConvertFrom-Json).dependencies
$dependencies = @{}
foreach ($package in @('com.unity.render-pipelines.universal','com.unity.cloud.gltfast','com.unity.ugui','com.unity.inputsystem')) { $dependencies[$package] = $projectDependencies.$package }
foreach ($module in @('physics','imgui','animation','imageconversion','audio')) { $dependencies['com.unity.modules.' + $module] = '1.0.0' }
@{ dependencies = $dependencies } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $validation 'Packages/manifest.json') -Encoding utf8
$validationLog = Join-Path $repository 'Logs/TerminalPrefabValidation.log'
$arguments = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod TerminalPrefabValidation.Run -logFile "{1}"' -f $validation,$validationLog
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output ('Unity prefab validation process: {0}. Log: {1}' -f $process.Id,$validationLog)
