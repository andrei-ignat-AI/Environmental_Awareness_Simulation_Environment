using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[DisallowMultipleComponent]
public class SimpleSampleExporter : MonoBehaviour
{
    [Serializable]
    private struct RendererMaterialState
    {
        public Renderer renderer;
        public Material[] sharedMaterials;
    }

    private delegate bool RendererVoxelFilter(Renderer renderer);

    private const string DefaultOutputRoot = "GeneratedSamples/DepthCaptures";
    private const string DefaultSideCameraName = "SideRgbCam";
    private const string SideRgbFileName = "cam_side_rgb.png";

    private readonly List<Bounds> voxelBounds = new List<Bounds>();
    private readonly Collider[] voxelOverlapBuffer = new Collider[64];
    private Material prototypeDepthMaterial;

    [Header("Dependencies")]
    public FixedSensorRig sensorRig;
    public Camera sideRgbCamera;
    public SimpleSceneCycleController sceneController;
    public RobotPoseController poseController;
    public SimplePropSpawner propSpawner;
    public SimpleForbiddenZones forbiddenZones;

    [Header("Output")]
    public string relativeOutputRoot = DefaultOutputRoot;
    public bool logExports = true;

    [Header("Depth Capture")]
    public Shader prototypeDepthShader;

    [Header("Voxel Grid")]
    public float voxelSizeMeters = 0.05f;

    [Header("Runtime Debug")]
    [SerializeField] private string lastExportDirectory = "";
    [SerializeField] [TextArea(4, 10)] private string lastExportSummary = "";

    public string LastExportDirectory
    {
        get { return lastExportDirectory; }
    }

    private void Awake()
    {
        AutoResolveDependencies();
    }

    private void OnValidate()
    {
        AutoResolveDependencies();
    }

    [ContextMenu("Export Latest Accepted Simple Sample")]
    public void ExportLatestAcceptedSample()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("SimpleSampleExporter: Enter Play Mode before exporting.");
            return;
        }

        if (!TryExportLatestAcceptedSample(out string summary))
        {
            Debug.LogWarning("SimpleSampleExporter: " + summary);
        }
    }

    public bool TryExportLatestAcceptedSample(out string summary)
    {
        AutoResolveDependencies();

        if (sceneController == null || !sceneController.LastGenerationAccepted)
        {
            summary = "no_accepted_sample_ready";
            return false;
        }

        return TryExportAcceptedSample(sceneController.LastAcceptedSampleIndex, out summary);
    }

    public bool TryExportAcceptedSample(int sampleIndex, out string summary)
    {
        AutoResolveDependencies();
        FixedSensorRig.CameraRigPreset cameraRig = sensorRig != null
            ? sensorRig.ActiveCameraRig
            : FixedSensorRig.CameraRigPreset.Cam4Classic;
        return TryExportAcceptedSample(sampleIndex, cameraRig, FixedSensorRig.GetRigExportId(cameraRig), out summary);
    }

    public bool TryExportAcceptedSample(
        int sampleIndex,
        FixedSensorRig.CameraRigPreset cameraRig,
        string cameraCaptureMode,
        out string summary)
    {
        AutoResolveDependencies();

        if (!ValidateDependencies(out summary))
        {
            return false;
        }

        if (sampleIndex < 0)
        {
            summary = "invalid_sample_index";
            return false;
        }

        string sampleName = "sample_" + sampleIndex.ToString("D4");
        sensorRig.ApplyCameraRigPreset(cameraRig, cameraCaptureMode);
        string layoutFolderName = ResolveLayoutFolderName();
        string cameraRigFolderName = sensorRig.ActiveCameraRigId;
        string timestampUtc = DateTime.UtcNow.ToString("O");
        string sampleDirectory = PrepareSampleDirectory(cameraRigFolderName, layoutFolderName, sampleName);

        Camera[] rigCameras = sensorRig.GetOrderedCameras();
        if (rigCameras.Length == 0)
        {
            summary = "depth_rig_cameras_missing";
            return false;
        }

        Physics.SyncTransforms();

        DepthMetadataExport depthMetadata = sensorRig.BuildDepthMetadata(sampleIndex, sampleName, timestampUtc);
        if (!TryCaptureRigDepthExports(rigCameras, depthMetadata, sampleDirectory, out summary))
        {
            return false;
        }

        WriteBytes(Path.Combine(sampleDirectory, SideRgbFileName), CaptureRgbPng(sideRgbCamera, sensorRig.width, sensorRig.height));

        CanonicalRobotStateExport robotStateExport = poseController.CaptureCanonicalStateExport();
        SceneObjectsExport sceneObjectsExport = BuildSceneObjectsExport();

        byte[] propVoxelBytes;
        SimpleVoxelMetadataExport propVoxelMetadata = BuildBoundsVoxelMetadata("voxel_props_occupancy.raw", "props", true, propSpawner != null ? propSpawner.activePropsRoot : null, out propVoxelBytes);
        byte[] robotVoxelBytes;
        SimpleVoxelMetadataExport robotVoxelMetadata = BuildRobotVoxelMetadata("voxel_robot_occupancy.raw", out robotVoxelBytes);
        byte[] sceneVoxelBytes;
        SimpleVoxelMetadataExport sceneVoxelMetadata = BuildSceneVoxelMetadata("voxel_scene_occupancy.raw", propVoxelBytes, robotVoxelBytes, out sceneVoxelBytes);

        int robotSeed = sceneController != null ? sceneController.LastAcceptedRobotSeed : -1;
        WriteJson(Path.Combine(sampleDirectory, "depth_metadata.json"), depthMetadata);
        WriteJson(Path.Combine(sampleDirectory, "robot_state.json"), robotStateExport);
        WriteJson(Path.Combine(sampleDirectory, "scene_objects.json"), sceneObjectsExport);
        WriteJson(Path.Combine(sampleDirectory, "voxel_metadata.json"), propVoxelMetadata);
        WriteJson(Path.Combine(sampleDirectory, "voxel_robot_metadata.json"), robotVoxelMetadata);
        WriteJson(Path.Combine(sampleDirectory, "voxel_scene_metadata.json"), sceneVoxelMetadata);

        WriteBytes(Path.Combine(sampleDirectory, propVoxelMetadata.fileName), propVoxelBytes);
        WriteBytes(Path.Combine(sampleDirectory, robotVoxelMetadata.fileName), robotVoxelBytes);
        WriteBytes(Path.Combine(sampleDirectory, sceneVoxelMetadata.fileName), sceneVoxelBytes);

        lastExportDirectory = sampleDirectory;
        lastExportSummary =
            "sample=" + sampleName +
            " rig=" + cameraRigFolderName +
            " layout=" + layoutFolderName +
            " depthCameras=" + rigCameras.Length +
            " robotSeed=" + robotSeed +
            " propsPlaced=" + (sceneController != null && sceneController.LastAcceptedScene != null ? sceneController.LastAcceptedScene.placedProps : 0) +
            " voxelProps=" + propVoxelMetadata.occupiedVoxels +
            " voxelRobot=" + robotVoxelMetadata.occupiedVoxels +
            " voxelScene=" + sceneVoxelMetadata.occupiedVoxels;

        if (logExports)
        {
            Debug.Log("SimpleSampleExporter: " + lastExportSummary + " path=" + sampleDirectory);
        }

        summary = lastExportSummary;
        return true;
    }

    private bool ValidateDependencies(out string summary)
    {
        if (sensorRig == null)
        {
            summary = "sensor_rig_missing";
            return false;
        }

        if (sideRgbCamera == null)
        {
            summary = "side_rgb_camera_missing";
            return false;
        }

        if (poseController == null)
        {
            summary = "pose_controller_missing";
            return false;
        }

        if (propSpawner == null)
        {
            summary = "simple_prop_spawner_missing";
            return false;
        }

        if (forbiddenZones == null)
        {
            summary = "simple_forbidden_zones_missing";
            return false;
        }

        if (prototypeDepthShader == null)
        {
            summary = "prototype_depth_shader_missing";
            return false;
        }

        summary = "ok";
        return true;
    }

    private string PrepareSampleDirectory(string cameraRigFolderName, string layoutFolderName, string sampleName)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string baseDirectory = Path.Combine(projectRoot, relativeOutputRoot);
        string rigDirectory = string.IsNullOrEmpty(cameraRigFolderName) ? baseDirectory : Path.Combine(baseDirectory, cameraRigFolderName);
        string layoutDirectory = string.IsNullOrEmpty(layoutFolderName) ? rigDirectory : Path.Combine(rigDirectory, layoutFolderName);
        string sampleDirectory = Path.Combine(layoutDirectory, sampleName);

        Directory.CreateDirectory(baseDirectory);
        Directory.CreateDirectory(rigDirectory);
        Directory.CreateDirectory(layoutDirectory);
        if (Directory.Exists(sampleDirectory))
        {
            Directory.Delete(sampleDirectory, true);
        }

        Directory.CreateDirectory(sampleDirectory);
        return sampleDirectory;
    }

    private string ResolveLayoutFolderName()
    {
        if (sceneController == null || sceneController.LastAcceptedScene == null)
        {
            return "";
        }

        string layoutFamily = sceneController.LastAcceptedScene.layoutFamily;
        if (string.IsNullOrEmpty(layoutFamily))
        {
            return "";
        }

        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidCharacters.Length; i++)
        {
            layoutFamily = layoutFamily.Replace(invalidCharacters[i], '_');
        }

        return layoutFamily;
    }

    private bool TryCaptureRigDepthExports(
        Camera[] rigCameras,
        DepthMetadataExport depthMetadata,
        string sampleDirectory,
        out string summary)
    {
        List<RendererMaterialState> rendererStates = OverrideSceneRenderersForDepth();
        try
        {
            return TryWriteDepthExports(rigCameras, depthMetadata, sampleDirectory, CaptureDepthMetersMaterialOverride, out summary);
        }
        finally
        {
            RestoreSceneRenderers(rendererStates);
        }
    }

    private bool TryWriteDepthExports(
        Camera[] rigCameras,
        DepthMetadataExport depthMetadata,
        string sampleDirectory,
        Func<Camera, int, int, float[]> captureDepth,
        out string summary)
    {
        for (int i = 0; i < rigCameras.Length; i++)
        {
            float[] depthValues = captureDepth(rigCameras[i], sensorRig.width, sensorRig.height);
            if (!ValidateDepthRange(depthValues, rigCameras[i], out string depthError))
            {
                summary = depthError;
                return false;
            }

            WriteFloatRaw(Path.Combine(sampleDirectory, depthMetadata.fullDepthRawFiles[i]), depthValues);
            WriteBytes(
                Path.Combine(sampleDirectory, depthMetadata.fullDepthPreviewFiles[i]),
                BuildDepthPreviewPng(depthValues, sensorRig.width, sensorRig.height, sensorRig.nearClip, sensorRig.farClip));
        }

        summary = "ok";
        return true;
    }

    private float[] CaptureDepthMetersMaterialOverride(Camera cameraComponent, int width, int height)
    {
        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = cameraComponent.targetTexture;
        bool previousEnabled = cameraComponent.enabled;
        CameraClearFlags previousClearFlags = cameraComponent.clearFlags;
        Color previousBackground = cameraComponent.backgroundColor;

        cameraComponent.targetTexture = renderTexture;
        cameraComponent.enabled = false;
        cameraComponent.clearFlags = CameraClearFlags.SolidColor;
        cameraComponent.backgroundColor = Color.black;
        cameraComponent.Render();

        RenderTexture.active = renderTexture;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RFloat, false);
        texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
        texture.Apply();

        Color[] pixels = texture.GetPixels();
        float[] depthValues = new float[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            float value = pixels[i].r;
            if (value < cameraComponent.nearClipPlane)
            {
                value = cameraComponent.farClipPlane;
            }

            depthValues[i] = Mathf.Clamp(value, cameraComponent.nearClipPlane, cameraComponent.farClipPlane);
        }

        cameraComponent.targetTexture = previousTarget;
        cameraComponent.enabled = previousEnabled;
        cameraComponent.clearFlags = previousClearFlags;
        cameraComponent.backgroundColor = previousBackground;
        RenderTexture.active = previousActive;

        RenderTexture.ReleaseTemporary(renderTexture);
        Destroy(texture);
        return depthValues;
    }

    private List<RendererMaterialState> OverrideSceneRenderersForDepth()
    {
        Material depthMaterial = GetPrototypeDepthMaterial();
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        List<RendererMaterialState> states = new List<RendererMaterialState>(renderers.Length);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            Material[] sharedMaterials = renderer.sharedMaterials;
            if (sharedMaterials == null || sharedMaterials.Length == 0)
            {
                continue;
            }

            states.Add(new RendererMaterialState
            {
                renderer = renderer,
                sharedMaterials = sharedMaterials,
            });

            Material[] replacement = new Material[sharedMaterials.Length];
            for (int materialIndex = 0; materialIndex < replacement.Length; materialIndex++)
            {
                replacement[materialIndex] = depthMaterial;
            }

            renderer.sharedMaterials = replacement;
        }

        return states;
    }

    private void RestoreSceneRenderers(List<RendererMaterialState> states)
    {
        for (int i = 0; i < states.Count; i++)
        {
            if (states[i].renderer != null)
            {
                states[i].renderer.sharedMaterials = states[i].sharedMaterials;
            }
        }
    }

    private Material GetPrototypeDepthMaterial()
    {
        if (prototypeDepthMaterial != null && prototypeDepthMaterial.shader == prototypeDepthShader)
        {
            return prototypeDepthMaterial;
        }

        if (prototypeDepthMaterial != null)
        {
            Destroy(prototypeDepthMaterial);
        }

        prototypeDepthMaterial = new Material(prototypeDepthShader);
        prototypeDepthMaterial.hideFlags = HideFlags.HideAndDontSave;
        return prototypeDepthMaterial;
    }

    private bool ValidateDepthRange(float[] depthValues, Camera cameraComponent, out string summary)
    {
        float minDepth = float.PositiveInfinity;
        float maxDepth = float.NegativeInfinity;

        for (int i = 0; i < depthValues.Length; i++)
        {
            float value = depthValues[i];
            if (!float.IsFinite(value))
            {
                summary = "depth_capture_invalid camera=" + cameraComponent.name + " reason=non_finite";
                return false;
            }

            if (value < minDepth)
            {
                minDepth = value;
            }

            if (value > maxDepth)
            {
                maxDepth = value;
            }
        }

        if (minDepth < cameraComponent.nearClipPlane || maxDepth > cameraComponent.farClipPlane)
        {
            summary =
                "depth_capture_invalid camera=" + cameraComponent.name +
                " min=" + minDepth.ToString("F4") +
                " max=" + maxDepth.ToString("F4") +
                " near=" + cameraComponent.nearClipPlane.ToString("F4") +
                " far=" + cameraComponent.farClipPlane.ToString("F4");
            return false;
        }

        if (minDepth >= cameraComponent.farClipPlane)
        {
            summary = "depth_capture_invalid camera=" + cameraComponent.name + " reason=no_geometry";
            return false;
        }

        summary = "ok";
        return true;
    }

    private byte[] CaptureRgbPng(Camera cameraComponent, int width, int height)
    {
        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false, false);

        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = cameraComponent.targetTexture;

        cameraComponent.targetTexture = renderTexture;
        cameraComponent.Render();

        RenderTexture.active = renderTexture;
        texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
        texture.Apply(false, false);
        byte[] pngBytes = texture.EncodeToPNG();

        cameraComponent.targetTexture = previousTarget;
        RenderTexture.active = previousActive;

        RenderTexture.ReleaseTemporary(renderTexture);
        Destroy(texture);
        return pngBytes;
    }

    private byte[] BuildDepthPreviewPng(float[] depthValues, int width, int height, float nearClip, float farClip)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false, false);
        Color32[] colors = new Color32[depthValues.Length];
        float maxDepth = 0f;

        for (int i = 0; i < depthValues.Length; i++)
        {
            float value = depthValues[i];
            if (value < farClip && value > maxDepth)
            {
                maxDepth = value;
            }
        }

        if (maxDepth <= 0f)
        {
            maxDepth = farClip;
        }

        for (int i = 0; i < depthValues.Length; i++)
        {
            float normalized = Mathf.Clamp01(depthValues[i] / maxDepth);
            byte value = (byte)Mathf.Clamp(Mathf.RoundToInt((1f - normalized) * 255f), 0, 255);
            colors[i] = new Color32(value, value, value, 255);
        }

        texture.SetPixels32(colors);
        texture.Apply(false, false);
        byte[] pngBytes = texture.EncodeToPNG();
        Destroy(texture);
        return pngBytes;
    }

    private SceneObjectsExport BuildSceneObjectsExport()
    {
        SceneObjectsExport export = new SceneObjectsExport();
        AppendRoomEntry(export);
        AppendTableEntries(export);
        AppendPropEntries(export);
        return export;
    }

    private void AppendRoomEntry(SceneObjectsExport export)
    {
        Bounds roomBounds;
        if (!forbiddenZones.TryGetRoomInteriorBounds(out roomBounds))
        {
            return;
        }

        export.objects.Add(new SceneObjectExport
        {
            objectId = "room_interior_bounds",
            semanticId = "room_interior_bounds",
            category = "room",
            surface = "Room",
            position = roomBounds.center,
            eulerRotation = Vector3.zero,
            scale = roomBounds.size,
            boundsSize = roomBounds.size,
        });
    }

    private void AppendTableEntries(SceneObjectsExport export)
    {
        if (forbiddenZones == null || forbiddenZones.tableRoot == null)
        {
            return;
        }

        Transform root = forbiddenZones.tableRoot.transform;
        AppendTableChildEntry(export, root, "table_top", "TableTop");
        AppendTableChildEntry(export, root, "rail_left", "Rail_Left");
        AppendTableChildEntry(export, root, "rail_right", "Rail_Right");
        AppendTableChildEntry(export, root, "pedestal", "Pedestal");
        AppendTableChildEntry(export, root, "table_base", "Base");
    }

    private void AppendTableChildEntry(SceneObjectsExport export, Transform tableRoot, string objectId, string childName)
    {
        Transform child = tableRoot.Find(childName);
        if (child == null)
        {
            Debug.LogWarning("SimpleSampleExporter: table child missing from metadata export: " + childName);
            return;
        }

        Bounds bounds;
        if (!TryGetCombinedBounds(child, out bounds))
        {
            Debug.LogWarning("SimpleSampleExporter: table child has no renderer/collider bounds: " + childName);
            return;
        }

        AppendFixtureEntry(
            export,
            objectId,
            "table",
            "Table",
            bounds.center,
            child.eulerAngles,
            bounds.size);
    }

    private void AppendFixtureEntry(
        SceneObjectsExport export,
        string objectId,
        string category,
        string surface,
        Vector3 position,
        Vector3 eulerRotation,
        Vector3 size)
    {
        export.objects.Add(new SceneObjectExport
        {
            objectId = objectId,
            semanticId = objectId,
            category = category,
            surface = surface,
            position = position,
            eulerRotation = eulerRotation,
            scale = size,
            boundsSize = size,
        });
    }

    private void AppendPropEntries(SceneObjectsExport export)
    {
        if (propSpawner == null || propSpawner.activePropsRoot == null)
        {
            return;
        }

        for (int i = 0; i < propSpawner.activePropsRoot.childCount; i++)
        {
            Transform child = propSpawner.activePropsRoot.GetChild(i);
            Bounds bounds;
            if (!TryGetCombinedBounds(child, out bounds))
            {
                continue;
            }

            string propId = ExtractPropId(child.name);
            SimplePropMetadata metadata = child.GetComponent<SimplePropMetadata>();
            export.objects.Add(new SceneObjectExport
            {
                objectId = child.name,
                semanticId = propId,
                category = propId,
                surface = InferSurfaceLabel(propId),
                position = child.position,
                eulerRotation = child.eulerAngles,
                scale = child.lossyScale,
                boundsSize = bounds.size,
                clinicalConfig = metadata != null ? metadata.clinicalConfig : "",
                layoutFamily = metadata != null ? metadata.layoutFamily : "",
                primaryOperatorSideZ = metadata != null ? metadata.primaryOperatorSideZ : 0,
                semanticZone = metadata != null ? metadata.semanticZone : "",
                targetBoundsMeters = metadata != null ? metadata.targetBoundsMeters : Vector3.zero,
                sampledJitterMeters = metadata != null ? metadata.sampledJitterMeters : Vector3.zero,
                sampledYawJitterDegrees = metadata != null ? metadata.sampledYawJitterDegrees : 0f,
                associatedWith = metadata != null ? metadata.associatedWith : "",
            });
        }
    }

    private SimpleVoxelMetadataExport BuildBoundsVoxelMetadata(string fileName, string contents, bool propsOnly, Transform root, out byte[] occupancy)
    {
        Bounds roomBounds;
        float voxelSize;
        int sizeX;
        int sizeY;
        int sizeZ;
        if (!TryCreateVoxelGrid(out roomBounds, out voxelSize, out sizeX, out sizeY, out sizeZ))
        {
            occupancy = new byte[0];
            return BuildVoxelMetadataRecord(fileName, contents, propsOnly, 0, 0, 0, Vector3.zero, Vector3.one * Mathf.Max(0.001f, voxelSizeMeters), occupancy);
        }

        occupancy = new byte[sizeX * sizeY * sizeZ];
        VoxelizeVisualMeshesOrBounds(root, roomBounds, voxelSize, sizeX, sizeY, sizeZ, occupancy, null);

        return BuildVoxelMetadataRecord(fileName, contents, propsOnly, sizeX, sizeY, sizeZ, roomBounds.min, Vector3.one * voxelSize, occupancy);
    }

    private SimpleVoxelMetadataExport BuildRobotVoxelMetadata(string fileName, out byte[] occupancy)
    {
        Bounds roomBounds;
        float voxelSize;
        int sizeX;
        int sizeY;
        int sizeZ;
        if (!TryCreateVoxelGrid(out roomBounds, out voxelSize, out sizeX, out sizeY, out sizeZ))
        {
            occupancy = new byte[0];
            return BuildVoxelMetadataRecord(fileName, "robot", false, 0, 0, 0, Vector3.zero, Vector3.one * Mathf.Max(0.001f, voxelSizeMeters), occupancy);
        }

        occupancy = new byte[sizeX * sizeY * sizeZ];
        VoxelizeVisualMeshesOrBounds(poseController != null && poseController.robotRoot != null ? poseController.robotRoot.transform : null, roomBounds, voxelSize, sizeX, sizeY, sizeZ, occupancy, IsRobotVisualRenderer);
        return BuildVoxelMetadataRecord(fileName, "robot", false, sizeX, sizeY, sizeZ, roomBounds.min, Vector3.one * voxelSize, occupancy);
    }

    private SimpleVoxelMetadataExport BuildSceneVoxelMetadata(string fileName, byte[] propOccupancy, byte[] robotOccupancy, out byte[] occupancy)
    {
        Bounds roomBounds;
        float voxelSize;
        int sizeX;
        int sizeY;
        int sizeZ;
        if (!TryCreateVoxelGrid(out roomBounds, out voxelSize, out sizeX, out sizeY, out sizeZ))
        {
            occupancy = new byte[0];
            return BuildVoxelMetadataRecord(fileName, "scene", false, 0, 0, 0, Vector3.zero, Vector3.one * Mathf.Max(0.001f, voxelSizeMeters), occupancy);
        }

        occupancy = new byte[sizeX * sizeY * sizeZ];
        UnionOccupancy(occupancy, propOccupancy);
        UnionOccupancy(occupancy, robotOccupancy);

        VoxelizeVisualMeshesOrBounds(forbiddenZones != null && forbiddenZones.tableRoot != null ? forbiddenZones.tableRoot.transform : null, roomBounds, voxelSize, sizeX, sizeY, sizeZ, occupancy, null);

        return BuildVoxelMetadataRecord(fileName, "scene", false, sizeX, sizeY, sizeZ, roomBounds.min, Vector3.one * voxelSize, occupancy);
    }

    private bool TryCreateVoxelGrid(out Bounds roomBounds, out float voxelSize, out int sizeX, out int sizeY, out int sizeZ)
    {
        roomBounds = new Bounds(Vector3.zero, Vector3.zero);
        voxelSize = Mathf.Max(0.001f, voxelSizeMeters);
        sizeX = 0;
        sizeY = 0;
        sizeZ = 0;

        if (forbiddenZones == null || !forbiddenZones.TryGetRoomInteriorBounds(out roomBounds))
        {
            return false;
        }

        sizeX = Mathf.Max(1, Mathf.CeilToInt(roomBounds.size.x / voxelSize));
        sizeY = Mathf.Max(1, Mathf.CeilToInt(roomBounds.size.y / voxelSize));
        sizeZ = Mathf.Max(1, Mathf.CeilToInt(roomBounds.size.z / voxelSize));
        return true;
    }

    private SimpleVoxelMetadataExport BuildVoxelMetadataRecord(
        string fileName,
        string contents,
        bool propsOnly,
        int sizeX,
        int sizeY,
        int sizeZ,
        Vector3 origin,
        Vector3 voxelSize,
        byte[] occupancy)
    {
        int occupied = 0;
        if (occupancy != null)
        {
            for (int i = 0; i < occupancy.Length; i++)
            {
                if (occupancy[i] != 0)
                {
                    occupied++;
                }
            }
        }

        return new SimpleVoxelMetadataExport
        {
            fileName = fileName,
            contents = contents,
            sizeX = sizeX,
            sizeY = sizeY,
            sizeZ = sizeZ,
            origin = origin,
            voxelSize = voxelSize,
            occupiedVoxels = occupied,
            propsOnly = propsOnly,
        };
    }

    private static void UnionOccupancy(byte[] target, byte[] source)
    {
        if (target == null || source == null)
        {
            return;
        }

        int length = Mathf.Min(target.Length, source.Length);
        for (int i = 0; i < length; i++)
        {
            if (source[i] != 0)
            {
                target[i] = 1;
            }
        }
    }

    private void VoxelizeVisualMeshesOrBounds(
        Transform root,
        Bounds roomBounds,
        float voxelSize,
        int sizeX,
        int sizeY,
        int sizeZ,
        byte[] occupancy,
        RendererVoxelFilter filter)
    {
        if (root == null || occupancy == null)
        {
            return;
        }

        int rasterizedMeshes = VoxelizeVisibleMeshes(root, roomBounds, voxelSize, sizeX, sizeY, sizeZ, occupancy, filter);
        if (rasterizedMeshes > 0)
        {
            return;
        }

        voxelBounds.Clear();
        CollectBounds(root, voxelBounds);
        for (int i = 0; i < voxelBounds.Count; i++)
        {
            RasterizeBounds(voxelBounds[i], roomBounds, voxelSize, sizeX, sizeY, sizeZ, occupancy);
        }
    }

    private int VoxelizeVisibleMeshes(
        Transform root,
        Bounds roomBounds,
        float voxelSize,
        int sizeX,
        int sizeY,
        int sizeZ,
        byte[] occupancy,
        RendererVoxelFilter filter)
    {
        if (root == null || occupancy == null)
        {
            return 0;
        }

        List<GameObject> temporaryObjects = new List<GameObject>();
        List<Mesh> temporaryMeshes = new List<Mesh>();
        List<MeshCollider> temporaryColliders = new List<MeshCollider>();
        List<Bounds> sourceBounds = new List<Bounds>();
        int rasterizedMeshes = 0;

        try
        {
            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter meshFilter = meshFilters[i];
                Renderer renderer = meshFilter != null ? meshFilter.GetComponent<Renderer>() : null;
                if (!IsVoxelizableMeshFilter(meshFilter, renderer, filter))
                {
                    continue;
                }

                MeshCollider collider = CreateTemporaryMeshCollider(
                    "VoxelCollider_" + meshFilter.name,
                    meshFilter.sharedMesh,
                    meshFilter.transform.position,
                    meshFilter.transform.rotation,
                    meshFilter.transform.lossyScale,
                    temporaryObjects);

                temporaryColliders.Add(collider);
                sourceBounds.Add(renderer.bounds);
            }

            SkinnedMeshRenderer[] skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinnedRenderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = skinnedRenderers[i];
                if (!IsVoxelizableRenderer(renderer, filter) || renderer.sharedMesh == null)
                {
                    continue;
                }

                Mesh bakedMesh = new Mesh();
                renderer.BakeMesh(bakedMesh);
                temporaryMeshes.Add(bakedMesh);

                MeshCollider collider = CreateTemporaryMeshCollider(
                    "VoxelCollider_" + renderer.name,
                    bakedMesh,
                    renderer.transform.position,
                    renderer.transform.rotation,
                    renderer.transform.lossyScale,
                    temporaryObjects);

                temporaryColliders.Add(collider);
                sourceBounds.Add(renderer.bounds);
            }

            Physics.SyncTransforms();

            for (int i = 0; i < temporaryColliders.Count; i++)
            {
                RasterizeMeshCollider(temporaryColliders[i], sourceBounds[i], roomBounds, voxelSize, sizeX, sizeY, sizeZ, occupancy);
                rasterizedMeshes++;
            }
        }
        finally
        {
            for (int i = 0; i < temporaryObjects.Count; i++)
            {
                DestroyTemporaryObject(temporaryObjects[i]);
            }

            for (int i = 0; i < temporaryMeshes.Count; i++)
            {
                DestroyTemporaryMesh(temporaryMeshes[i]);
            }

            Physics.SyncTransforms();
        }

        return rasterizedMeshes;
    }

    private static MeshCollider CreateTemporaryMeshCollider(
        string name,
        Mesh mesh,
        Vector3 position,
        Quaternion rotation,
        Vector3 lossyScale,
        List<GameObject> temporaryObjects)
    {
        GameObject temporary = new GameObject(name);
        temporary.hideFlags = HideFlags.HideAndDontSave;
        temporary.transform.SetPositionAndRotation(position, rotation);
        temporary.transform.localScale = lossyScale;

        MeshCollider collider = temporary.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = false;
        collider.enabled = true;

        temporaryObjects.Add(temporary);
        return collider;
    }

    private static bool IsVoxelizableMeshFilter(MeshFilter meshFilter, Renderer renderer, RendererVoxelFilter filter)
    {
        return meshFilter != null && meshFilter.sharedMesh != null && IsVoxelizableRenderer(renderer, filter);
    }

    private static bool IsVoxelizableRenderer(Renderer renderer, RendererVoxelFilter filter)
    {
        if (renderer == null)
        {
            return false;
        }

        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
        {
            return false;
        }

        return filter == null || filter(renderer);
    }

    private bool IsRobotVisualRenderer(Renderer renderer)
    {
        if (renderer == null)
        {
            return false;
        }

        Transform robotRoot = poseController != null && poseController.robotRoot != null ? poseController.robotRoot.transform : null;
        if (robotRoot == null)
        {
            return false;
        }

        bool underVisuals = false;
        Transform current = renderer.transform;
        while (current != null && current != robotRoot.parent)
        {
            string objectName = current.name;
            if (objectName == "GeneratedSimpleProxies" || objectName == "GeneratedRobotCollisionRig" || objectName == "Collisions")
            {
                return false;
            }

            if (objectName == "Visuals")
            {
                underVisuals = true;
            }

            if (current == robotRoot)
            {
                break;
            }

            current = current.parent;
        }

        return underVisuals;
    }

    private void RasterizeMeshCollider(
        MeshCollider collider,
        Bounds bounds,
        Bounds roomBounds,
        float voxelSize,
        int sizeX,
        int sizeY,
        int sizeZ,
        byte[] occupancy)
    {
        if (collider == null || collider.sharedMesh == null)
        {
            return;
        }

        int minX;
        int minY;
        int minZ;
        int maxX;
        int maxY;
        int maxZ;
        if (!TryGetVoxelRange(bounds, roomBounds, voxelSize, sizeX, sizeY, sizeZ, 1, out minX, out minY, out minZ, out maxX, out maxY, out maxZ))
        {
            return;
        }

        int localSizeX = maxX - minX + 1;
        int localSizeY = maxY - minY + 1;
        int localSizeZ = maxZ - minZ + 1;
        byte[] surface = new byte[localSizeX * localSizeY * localSizeZ];
        Vector3 halfExtents = Vector3.one * (voxelSize * 0.5f);
        bool hasSurface = false;

        for (int y = 0; y < localSizeY; y++)
        {
            for (int z = 0; z < localSizeZ; z++)
            {
                for (int x = 0; x < localSizeX; x++)
                {
                    Vector3 center = VoxelCenter(roomBounds, voxelSize, minX + x, minY + y, minZ + z);
                    if (!DoesVoxelTouchCollider(center, halfExtents, collider))
                    {
                        continue;
                    }

                    surface[GridIndex(x, y, z, localSizeX, localSizeZ)] = 1;
                    hasSurface = true;
                }
            }
        }

        if (!hasSurface)
        {
            return;
        }

        byte[] solid = DilateOccupancy(surface, localSizeX, localSizeY, localSizeZ);
        byte[] exterior = BuildExteriorMask(solid, localSizeX, localSizeY, localSizeZ);

        for (int y = 0; y < localSizeY; y++)
        {
            for (int z = 0; z < localSizeZ; z++)
            {
                for (int x = 0; x < localSizeX; x++)
                {
                    int localIndex = GridIndex(x, y, z, localSizeX, localSizeZ);
                    if (solid[localIndex] == 0 && exterior[localIndex] != 0)
                    {
                        continue;
                    }

                    int globalX = minX + x;
                    int globalY = minY + y;
                    int globalZ = minZ + z;
                    occupancy[GridIndex(globalX, globalY, globalZ, sizeX, sizeZ)] = 1;
                }
            }
        }
    }

    private bool DoesVoxelTouchCollider(Vector3 center, Vector3 halfExtents, MeshCollider target)
    {
        int hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, voxelOverlapBuffer, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hitCount && i < voxelOverlapBuffer.Length; i++)
        {
            if (voxelOverlapBuffer[i] == target)
            {
                return true;
            }
        }

        if (hitCount < voxelOverlapBuffer.Length)
        {
            return false;
        }

        Collider[] allHits = Physics.OverlapBox(center, halfExtents, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < allHits.Length; i++)
        {
            if (allHits[i] == target)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] DilateOccupancy(byte[] source, int sizeX, int sizeY, int sizeZ)
    {
        byte[] dilated = (byte[])source.Clone();

        for (int y = 0; y < sizeY; y++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    if (source[GridIndex(x, y, z, sizeX, sizeZ)] == 0)
                    {
                        continue;
                    }

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;
                                int nz = z + dz;
                                if (nx < 0 || nx >= sizeX || ny < 0 || ny >= sizeY || nz < 0 || nz >= sizeZ)
                                {
                                    continue;
                                }

                                dilated[GridIndex(nx, ny, nz, sizeX, sizeZ)] = 1;
                            }
                        }
                    }
                }
            }
        }

        return dilated;
    }

    private static byte[] BuildExteriorMask(byte[] solid, int sizeX, int sizeY, int sizeZ)
    {
        byte[] exterior = new byte[solid.Length];
        Queue<int> queue = new Queue<int>();

        for (int y = 0; y < sizeY; y++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    bool boundary = x == 0 || y == 0 || z == 0 || x == sizeX - 1 || y == sizeY - 1 || z == sizeZ - 1;
                    if (!boundary)
                    {
                        continue;
                    }

                    int index = GridIndex(x, y, z, sizeX, sizeZ);
                    if (solid[index] != 0 || exterior[index] != 0)
                    {
                        continue;
                    }

                    exterior[index] = 1;
                    queue.Enqueue(index);
                }
            }
        }

        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int x;
            int y;
            int z;
            DecodeGridIndex(index, sizeX, sizeZ, out x, out y, out z);

            TryMarkExterior(x + 1, y, z, solid, exterior, queue, sizeX, sizeY, sizeZ);
            TryMarkExterior(x - 1, y, z, solid, exterior, queue, sizeX, sizeY, sizeZ);
            TryMarkExterior(x, y + 1, z, solid, exterior, queue, sizeX, sizeY, sizeZ);
            TryMarkExterior(x, y - 1, z, solid, exterior, queue, sizeX, sizeY, sizeZ);
            TryMarkExterior(x, y, z + 1, solid, exterior, queue, sizeX, sizeY, sizeZ);
            TryMarkExterior(x, y, z - 1, solid, exterior, queue, sizeX, sizeY, sizeZ);
        }

        return exterior;
    }

    private static void TryMarkExterior(
        int x,
        int y,
        int z,
        byte[] solid,
        byte[] exterior,
        Queue<int> queue,
        int sizeX,
        int sizeY,
        int sizeZ)
    {
        if (x < 0 || x >= sizeX || y < 0 || y >= sizeY || z < 0 || z >= sizeZ)
        {
            return;
        }

        int index = GridIndex(x, y, z, sizeX, sizeZ);
        if (solid[index] != 0 || exterior[index] != 0)
        {
            return;
        }

        exterior[index] = 1;
        queue.Enqueue(index);
    }

    private void CollectBounds(Transform root, List<Bounds> results)
    {
        if (root == null)
        {
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            results.Add(renderer.bounds);
        }

        if (renderers.Length > 0)
        {
            return;
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            results.Add(collider.bounds);
        }
    }

    private void RasterizeBounds(Bounds bounds, Bounds roomBounds, float voxelSize, int sizeX, int sizeY, int sizeZ, byte[] occupancy)
    {
        int minX;
        int minY;
        int minZ;
        int maxX;
        int maxY;
        int maxZ;
        if (!TryGetVoxelRange(bounds, roomBounds, voxelSize, sizeX, sizeY, sizeZ, 0, out minX, out minY, out minZ, out maxX, out maxY, out maxZ))
        {
            return;
        }

        for (int y = minY; y <= maxY; y++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    occupancy[GridIndex(x, y, z, sizeX, sizeZ)] = 1;
                }
            }
        }
    }

    private static bool TryGetVoxelRange(
        Bounds bounds,
        Bounds roomBounds,
        float voxelSize,
        int sizeX,
        int sizeY,
        int sizeZ,
        int paddingCells,
        out int minX,
        out int minY,
        out int minZ,
        out int maxX,
        out int maxY,
        out int maxZ)
    {
        minX = 0;
        minY = 0;
        minZ = 0;
        maxX = -1;
        maxY = -1;
        maxZ = -1;

        if (bounds.size == Vector3.zero)
        {
            return false;
        }

        minX = Mathf.FloorToInt((bounds.min.x - roomBounds.min.x) / voxelSize) - paddingCells;
        minY = Mathf.FloorToInt((bounds.min.y - roomBounds.min.y) / voxelSize) - paddingCells;
        minZ = Mathf.FloorToInt((bounds.min.z - roomBounds.min.z) / voxelSize) - paddingCells;
        maxX = Mathf.CeilToInt((bounds.max.x - roomBounds.min.x) / voxelSize) - 1 + paddingCells;
        maxY = Mathf.CeilToInt((bounds.max.y - roomBounds.min.y) / voxelSize) - 1 + paddingCells;
        maxZ = Mathf.CeilToInt((bounds.max.z - roomBounds.min.z) / voxelSize) - 1 + paddingCells;

        if (maxX < 0 || maxY < 0 || maxZ < 0 || minX >= sizeX || minY >= sizeY || minZ >= sizeZ)
        {
            return false;
        }

        minX = Mathf.Clamp(minX, 0, sizeX - 1);
        minY = Mathf.Clamp(minY, 0, sizeY - 1);
        minZ = Mathf.Clamp(minZ, 0, sizeZ - 1);
        maxX = Mathf.Clamp(maxX, 0, sizeX - 1);
        maxY = Mathf.Clamp(maxY, 0, sizeY - 1);
        maxZ = Mathf.Clamp(maxZ, 0, sizeZ - 1);

        return minX <= maxX && minY <= maxY && minZ <= maxZ;
    }

    private static Vector3 VoxelCenter(Bounds roomBounds, float voxelSize, int x, int y, int z)
    {
        return roomBounds.min + new Vector3(
            (x + 0.5f) * voxelSize,
            (y + 0.5f) * voxelSize,
            (z + 0.5f) * voxelSize);
    }

    private static int GridIndex(int x, int y, int z, int sizeX, int sizeZ)
    {
        return x + (sizeX * (z + (sizeZ * y)));
    }

    private static void DecodeGridIndex(int index, int sizeX, int sizeZ, out int x, out int y, out int z)
    {
        int layerSize = sizeX * sizeZ;
        y = index / layerSize;
        int remainder = index - (y * layerSize);
        z = remainder / sizeX;
        x = remainder - (z * sizeX);
    }

    private static void DestroyTemporaryObject(GameObject temporary)
    {
        if (temporary == null)
        {
            return;
        }

#if UNITY_EDITOR
        DestroyImmediate(temporary);
#else
        Destroy(temporary);
#endif
    }

    private static void DestroyTemporaryMesh(Mesh mesh)
    {
        if (mesh == null)
        {
            return;
        }

#if UNITY_EDITOR
        DestroyImmediate(mesh);
#else
        Destroy(mesh);
#endif
    }

    private static string ExtractPropId(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return "prop";
        }

        int secondUnderscore = objectName.IndexOf('_');
        if (secondUnderscore >= 0)
        {
            secondUnderscore = objectName.IndexOf('_', secondUnderscore + 1);
        }

        return secondUnderscore >= 0 && secondUnderscore + 1 < objectName.Length
            ? objectName.Substring(secondUnderscore + 1)
            : objectName;
    }

    private static string InferSurfaceLabel(string propId)
    {
        switch (propId)
        {
            case "ceiling_short":
            case "ceiling_long":
            case "ceiling_lamp":
                return "Ceiling";
            case "patient":
                return "Table";
            case "uhd_tv":
                return "Wall";
            case "table_block":
                return "Table";
            default:
                return "Floor";
        }
    }

    private static bool TryGetCombinedBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds)
        {
            return true;
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    private static void WriteFloatRaw(string path, float[] values)
    {
        byte[] bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonUtility.ToJson(value, true));
    }

    private void AutoResolveDependencies()
    {
        if (sensorRig == null)
        {
            sensorRig = GetComponent<FixedSensorRig>();
            if (sensorRig == null)
            {
                sensorRig = FindAnyObjectByType<FixedSensorRig>();
            }
        }

        if (sideRgbCamera == null)
        {
            Transform child = transform.Find(DefaultSideCameraName);
            if (child != null)
            {
                sideRgbCamera = child.GetComponent<Camera>();
            }

            if (sideRgbCamera == null)
            {
                Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                for (int i = 0; i < cameras.Length; i++)
                {
                    if (cameras[i] != null && cameras[i].name == DefaultSideCameraName)
                    {
                        sideRgbCamera = cameras[i];
                        break;
                    }
                }
            }
        }

        if (sceneController == null)
        {
            sceneController = FindAnyObjectByType<SimpleSceneCycleController>();
        }

        if (poseController == null)
        {
            poseController = FindAnyObjectByType<RobotPoseController>();
        }

        if (propSpawner == null)
        {
            propSpawner = FindAnyObjectByType<SimplePropSpawner>();
        }

        if (forbiddenZones == null)
        {
            forbiddenZones = FindAnyObjectByType<SimpleForbiddenZones>();
        }

        if (prototypeDepthShader == null)
        {
            prototypeDepthShader = Shader.Find("Hidden/SimpleTrueDepthPrototype");
        }
    }

    private void OnDestroy()
    {
        if (prototypeDepthMaterial != null)
        {
            Destroy(prototypeDepthMaterial);
            prototypeDepthMaterial = null;
        }
    }
}
