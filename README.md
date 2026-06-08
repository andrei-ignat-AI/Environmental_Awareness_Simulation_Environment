# Environmental Awareness Simulation Environment

Clean Unity project for generating Philips Azurion-style simulation samples used by the environmental-awareness Python pipeline.

## Requirements

- Unity `6000.3.9f1`
- Git with SSH access for cloning/pushing
- Internet access on first open so Unity can resolve Git/registry packages from `Packages/manifest.json`

Open the project by selecting this repository root in Unity Hub. The Unity project root is this folder directly; there is no nested Unity project.

On macOS:

```bash
git clone git@github.com:andrei-ignat-AI/Environmental_Awareness_Simulation_Environment.git
```

On Windows:

```powershell
git clone git@github.com:andrei-ignat-AI/Environmental_Awareness_Simulation_Environment.git
```

Then open `Environmental_Awareness_Simulation_Environment/` with Unity `6000.3.9f1`.

## Main Scene

The active scene is:

```text
Assets/Scenes/SampleScene_Simple.unity
```

It is also the enabled scene in `ProjectSettings/EditorBuildSettings.asset`.

The scene contains the clean room, surgery table, FlexArm robot, sensor rig, deterministic prop placement, and export runner. Press Play to run the current simulation setup. The `SimpleAutoCaptureRunner` on `SensorRig` is configured to run on start and export `40` accepted generated scenes in `ComparisonOfAllConfigurations` mode. When Play Mode is idle, pressing Space generates the next runtime scene through `SimpleSceneCycleController`.

## Export Output

Runtime exports are written under:

```text
GeneratedSamples/DepthCaptures/
```

The folder is intentionally ignored by Git. In comparison mode, exports are grouped by camera rig, layout family, and sample:

```text
GeneratedSamples/DepthCaptures/<camera-rig>/<layout-family>/sample_0000/
```

Each exported sample keeps this file contract:

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

Depth values are written as raw float meters. Voxel occupancy files are raw occupancy bytes with grid metadata in the matching JSON files.

## Camera Rigs

`FixedSensorRig` defines four LUCID-style camera configurations:

- `4CamClassic`: four symmetric baseline corner cameras
- `3Cam`: three-camera minimum rig
- `4CamAsym`: asymmetric four-camera wall/cross rig
- `5Cam`: dense five-camera rig

`SimpleAutoCaptureRunner.cameraExportMode = ComparisonOfAllConfigurations` exports the same accepted generated scene through all four rigs before advancing to the next scene.

## Important Code Map

- `Assets/RoomSpawner.cs`: clean-room primitive builder
- `Assets/Robot_stock/SurgeryTableBuilder.cs`: procedural surgery table builder
- `Assets/Robot_stock/RobotStabilitySetup.cs`: robot articulation stabilization
- `Assets/Simulation/Robot/`: robot pose types, randomization, control, validation, and table avoidance
- `Assets/Simulation/Capture/FixedSensorRig.cs`: named camera rigs and depth metadata
- `Assets/SimulationSimple/SimpleSceneCycleController.cs`: Play Mode scene stepping and accepted-scene orchestration
- `Assets/SimulationSimple/SimplePropLibrary.cs`: deterministic clinical layout and prop specs
- `Assets/SimulationSimple/SimplePropSpawner.cs`: runtime prop placement and validation
- `Assets/SimulationSimple/SimpleSampleExporter.cs`: canonical depth, RGB, robot, scene-object, and voxel exports
- `Assets/SimulationSimple/SimpleAutoCaptureRunner.cs`: unattended batch capture/export loop

## Repository Notes

This repository intentionally excludes Python code, reports, generated sample outputs, Unity editor caches, local bridge helper scripts, and old Unity archive/export files. Unity `.meta` files are tracked with assets so GUIDs remain stable.
