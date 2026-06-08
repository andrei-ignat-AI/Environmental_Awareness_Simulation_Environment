# Environmental Awareness Simulation Environment

Clean Unity project for generating Philips Azurion-style simulation samples for the environmental-awareness pipeline.

## Requirements

- Unity `6000.3.9f1`
- Git
- Internet access on first open, so Unity can resolve registry and Git packages from `Packages/manifest.json`

The Unity project root is this repository folder directly. It is not nested inside another folder.

## Clone And Open

Clone the repository:

```bash
git clone git@github.com:andrei-ignat-AI/Environmental_Awareness_Simulation_Environment.git
cd Environmental_Awareness_Simulation_Environment
```

Open it in Unity Hub:

1. Open Unity Hub.
2. Go to `Projects`.
3. Select `Add`, then `Add project from disk`.
4. Select the repository root folder: `Environmental_Awareness_Simulation_Environment/`.
5. Open it with Unity `6000.3.9f1`.

Select the repository root folder, not `Assets/`, `ProjectSettings/`, or any generated output folder. Unity Hub uses the project folder that contains `Assets/`, `Packages/`, and `ProjectSettings/`.

If Hub warns that the saved Editor version is missing, install Unity `6000.3.9f1` from the Hub and open the project with that version. Opening with a different Unity version may trigger reimport or upgrade behavior.

Unity Hub reference:

- Add/open projects: https://docs.unity.com/en-us/hub/project-manage
- Project list, Editor version warnings, and Hub project details: https://docs.unity.com/en-us/hub/projects-window-reference

## Main Scene

The active scene is:

```text
Assets/Scenes/SampleScene_Simple.unity
```

It is also the enabled scene in:

```text
ProjectSettings/EditorBuildSettings.asset
```

After the first import, Unity should load the project normally. If Unity opens an empty scene or a temporary backup scene, open `Assets/Scenes/SampleScene_Simple.unity` from the Project window. You do not need to drag scene assets, prefabs, scripts, or models into the Hierarchy.

The scene contains the clean room, surgery table, FlexArm robot, sensor rig, deterministic prop placement, and export runner.

## Run Capture

Press Play in Unity.

The `SimpleAutoCaptureRunner` on `SensorRig` is configured to run automatically on Play:

- `runOnStart`: enabled
- `targetAcceptedSamples`: `40`
- `cameraExportMode`: `ComparisonOfAllConfigurations`

This means one full run exports 40 accepted generated scenes for each camera rig. With the current four rigs, that is 160 sample folders.

When the unattended run is not active, pressing Space generates the next runtime scene through `SimpleSceneCycleController`. The normal dataset workflow is the full Play Mode run, not manual drag-and-drop in the Hierarchy.

## Export Output

Runtime exports are written under:

```text
GeneratedSamples/DepthCaptures/
```

The folder layout is:

```text
GeneratedSamples/DepthCaptures/<camera-rig>/<layout-family>/sample_####/
```

Example:

```text
GeneratedSamples/DepthCaptures/4CamClassic/C1_standard_cath/sample_0000/
```

A complete comparison run currently produces:

- `4CamClassic`: 40 samples
- `3Cam`: 40 samples
- `4CamAsym`: 40 samples
- `5Cam`: 40 samples
- total: 160 sample folders, 2880 files

Generated outputs are intentionally ignored by Git. They are local products of running the simulation, not source files required to open the Unity project.

Each sample exports:

```text
depth_metadata.json
robot_state.json
scene_objects.json
cam{i}_depth.raw
cam{i}_depth_vis.png
cam_side_rgb.png
voxel_props_occupancy.raw
voxel_metadata.json
voxel_robot_occupancy.raw
voxel_robot_metadata.json
voxel_scene_occupancy.raw
voxel_scene_metadata.json
```

Depth values are raw float meters. Voxel occupancy files are raw occupancy bytes with grid metadata in the matching JSON files.

## Camera Rigs

`FixedSensorRig` defines four named camera configurations:

- `4CamClassic`: four symmetric baseline corner cameras
- `3Cam`: three-camera minimum rig
- `4CamAsym`: asymmetric four-camera wall/cross rig
- `5Cam`: dense five-camera rig

`ComparisonOfAllConfigurations` exports the same accepted generated scene through all four rigs before advancing to the next scene.

## Project Contents

Essential files and folders for another machine:

```text
Assets/
Packages/
ProjectSettings/
.gitignore
README.md
```

Do not remove Unity `.meta` files. Unity stores asset IDs and import settings in `.meta` files; losing them can break material, prefab, scene, and script references.

Unity asset metadata reference:

- Asset metadata and `.meta` files: https://docs.unity.cn/Manual/AssetMetadata.html

Local-only or generated state that should not be committed:

```text
GeneratedSamples/
GeneratedSamples_backup/
Library/
Temp/
Logs/
UserSettings/
.vscode/
*.csproj
*.sln
*.slnx
.DS_Store
```

Unity can regenerate `Library/` from the source assets and project settings, so it should stay out of version control.

Unity Asset Database reference:

- Asset cache and `Library/`: https://docs.unity.cn/Manual/AssetDatabase.html

## Important Code Map

- `Assets/RoomSpawner.cs`: clean-room primitive builder
- `Assets/Robot_stock/SurgeryTableBuilder.cs`: procedural surgery table builder
- `Assets/Robot_stock/RobotStabilitySetup.cs`: robot articulation stabilization
- `Assets/Simulation/Robot/`: robot pose types, randomization, control, validation, and table avoidance
- `Assets/Simulation/Capture/FixedSensorRig.cs`: named camera rigs and depth metadata
- `Assets/SimulationSimple/SimpleSceneCycleController.cs`: Play Mode scene stepping and accepted-scene orchestration
- `Assets/SimulationSimple/SimplePropLibrary.cs`: deterministic clinical layout and prop specs
- `Assets/SimulationSimple/SimplePropSpawner.cs`: runtime prop placement and validation
- `Assets/SimulationSimple/SimpleSampleExporter.cs`: depth, RGB, robot, scene-object, and voxel exports
- `Assets/SimulationSimple/SimpleAutoCaptureRunner.cs`: unattended batch capture/export loop

## Optional Import Check

For automated import or CI checks, use the installed Unity Editor binary with `-projectPath`, `-batchmode`, and `-quit`. Unity documents these command-line arguments here:

- Unity command-line arguments: https://docs.unity.cn/2017.2/Documentation/Manual/CommandLineArguments.html

Example on macOS:

```bash
/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity \
  -projectPath "$PWD" \
  -batchmode \
  -quit \
  -logFile -
```

Do not run batch import while the same project is already open in the Unity Editor.

## Repository Notes

This repository intentionally excludes Python code, reports, generated sample outputs, Unity editor caches, local bridge helper scripts, and old Unity archive/export files. Unity Bridge is not part of the clean committed project.
