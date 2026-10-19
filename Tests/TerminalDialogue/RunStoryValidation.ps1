param([Parameter(Mandatory = $true)][string]$UnityPath)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$editorDirectory = Split-Path -Parent $UnityPath
$mono = Join-Path $editorDirectory 'Data/MonoBleedingEdge/bin/mono.exe'
$compiler = Join-Path $editorDirectory 'Data/MonoBleedingEdge/lib/mono/4.5/csc.exe'
$outputDirectory = Join-Path $repository '.utmp/ReturnStoryValidation'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$commonSources = @(
    (Join-Path $repository 'Assets/Scripts/TerminalDialogue/TerminalDialogueData.cs'),
    (Join-Path $repository 'Assets/Scripts/TerminalDialogue/TerminalDialogueSession.cs')
)
$sessionOutput = Join-Path $outputDirectory 'SessionTests.exe'
& $mono $compiler /nologo /target:exe "/out:$sessionOutput" $commonSources (Join-Path $PSScriptRoot 'SessionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Session tests did not compile.' }
& $mono $sessionOutput
if ($LASTEXITCODE -ne 0) { throw 'Session tests failed.' }
$controllerOutput = Join-Path $outputDirectory 'ControllerStoryTests.exe'
$controllerSources = @(
    (Join-Path $repository 'Assets/Scripts/TerminalDialogue/TerminalDialogueController.cs'),
    (Join-Path $repository 'Assets/Scripts/TerminalDialogue/TerminalDialogueDefinition.cs'),
    (Join-Path $repository 'Assets/ReturnWindow/Editor/ReturnStoryBuilder.cs'),
    (Join-Path $repository 'Assets/ReturnWindow/Runtime/MissionSchedule.cs'),
    (Join-Path $PSScriptRoot 'UnityControllerStubs.cs'),
    (Join-Path $PSScriptRoot 'ControllerStoryTests.cs')
)
& $mono $compiler /nologo /target:exe "/out:$controllerOutput" $commonSources $controllerSources
if ($LASTEXITCODE -ne 0) { throw 'Controller/story tests did not compile.' }
& $mono $controllerOutput
if ($LASTEXITCODE -ne 0) { throw 'Controller/story tests failed.' }
