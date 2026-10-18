param([Parameter(Mandatory = $true)][string]$UnityPath)
$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validation = Join-Path $repository '.utmp/RetroWireframeValidation'
New-Item -ItemType Directory -Force -Path (Join-Path $validation 'Assets/Editor'),(Join-Path $validation 'Assets/Shaders'),(Join-Path $validation 'Packages'),(Join-Path $validation 'ProjectSettings'),(Join-Path $repository 'Logs') | Out-Null
foreach ($name in @('Xray.cs','Xray.cs.meta','rayURP.shader','rayURP.shader.meta','RetroWireframeMeshBuilder.cs','RetroWireframeMeshBuilder.cs.meta','XrayOutlineRenderer.cs','XrayOutlineRenderer.cs.meta')) {
    $source = Join-Path $repository ('Assets/ArtAssets/Shaders/' + $name)
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $validation ('Assets/Shaders/' + $name)) -Force }
}
New-Item -ItemType Directory -Force -Path (Join-Path $validation 'Assets/Shaders/Resources') | Out-Null
foreach ($relative in @('Resources.meta','Resources/XraySilhouette.shader.meta')) {
    $metadata = Join-Path $repository ('Assets/ArtAssets/Shaders/' + $relative)
    if ((Get-Content -LiteralPath $metadata -Raw) -notmatch '(?m)^guid: [0-9a-f]{32}\r?$') {
        throw ('Invalid Unity asset GUID: ' + $metadata)
    }
    Copy-Item -LiteralPath $metadata -Destination (Join-Path $validation ('Assets/Shaders/' + $relative)) -Force
}
Copy-Item -LiteralPath (Join-Path $repository 'Assets/ArtAssets/Shaders/Resources/XraySilhouette.shader') -Destination (Join-Path $validation 'Assets/Shaders/Resources/XraySilhouette.shader') -Force
Copy-Item -LiteralPath (Join-Path $repository 'Assets/Editor/XrayEditor.cs') -Destination (Join-Path $validation 'Assets/Editor/XrayEditor.cs') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EditorValidation.cs') -Destination (Join-Path $validation 'Assets/Editor/EditorValidation.cs') -Force
Copy-Item -LiteralPath (Join-Path $repository 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $validation 'ProjectSettings/ProjectVersion.txt') -Force
$projectDependencies = (Get-Content (Join-Path $repository 'Packages/manifest.json') -Raw | ConvertFrom-Json).dependencies
$dependencies = @{ 'com.unity.render-pipelines.universal' = $projectDependencies.'com.unity.render-pipelines.universal'; 'com.unity.modules.physics' = '1.0.0'; 'com.unity.modules.imgui' = '1.0.0' }
$referenceModel = Join-Path $repository 'Assets/ArtAssets/3DModels/生命装置.glb'
if (Test-Path -LiteralPath $referenceModel) {
    Copy-Item -LiteralPath $referenceModel -Destination (Join-Path $validation 'Assets/ReferenceModel.glb') -Force
    $dependencies['com.unity.cloud.gltfast'] = $projectDependencies.'com.unity.cloud.gltfast'
    $dependencies['com.unity.modules.animation'] = '1.0.0'
    $dependencies['com.unity.modules.imageconversion'] = '1.0.0'
}
@{ dependencies = $dependencies } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $validation 'Packages/manifest.json') -Encoding utf8
$validationLog = Join-Path $repository 'Logs/RetroWireframeValidation.log'
$arguments = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod RetroWireframeValidation.Run -logFile "{1}"' -f $validation,$validationLog
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output ('Unity validation process: {0}. Log: {1}' -f $process.Id,$validationLog)
