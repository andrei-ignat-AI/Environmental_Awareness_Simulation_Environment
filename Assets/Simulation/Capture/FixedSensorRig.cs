using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines named LUCID-style depth camera rigs for fair camera-placement comparisons.
/// Camera transforms are applied in code so scene layout, props, and robot motion stay independent.
/// </summary>
[DisallowMultipleComponent]
public class FixedSensorRig : MonoBehaviour
{
    public enum CameraRigPreset
    {
        Cam4Classic = 0,
        Cam3 = 1,
        Cam4Asym = 2,
        Cam5 = 3,
    }

    public enum ResolutionPreset
    {
        FullHD = 0,
        UHD4K = 1,
        Custom = 2,
        LUCID = 3,
    }

    private struct CameraDefinition
    {
        public string name;
        public string role;
        public string mountWall;
        public Vector3 position;
        public Vector3 lookAtTarget;
        public bool useExplicitEuler;
        public Vector3 eulerRotation;
        public bool isBaseline;

        public CameraDefinition(
            string name,
            string role,
            string mountWall,
            Vector3 position,
            Vector3 lookAtTarget,
            bool useExplicitEuler,
            Vector3 eulerRotation,
            bool isBaseline)
        {
            this.name = name;
            this.role = role;
            this.mountWall = mountWall;
            this.position = position;
            this.lookAtTarget = lookAtTarget;
            this.useExplicitEuler = useExplicitEuler;
            this.eulerRotation = eulerRotation;
            this.isBaseline = isBaseline;
        }
    }

    private static readonly CameraDefinition[] Cam4ClassicDefinitions =
    {
        new CameraDefinition("DepthCam_BL", "legacy_corner_back_left", "corner_minusX_minusZ", new Vector3(-4.5f, 1.35f, -2.5f), new Vector3(0f, 0.4f, 0f), true, new Vector3(10.4626548f, 60.9453964f, 0f), true),
        new CameraDefinition("DepthCam_BR", "legacy_corner_back_right", "corner_plusX_minusZ", new Vector3( 4.5f, 1.35f, -2.5f), new Vector3(0f, 0.4f, 0f), true, new Vector3(10.4626548f, 299.054596f, 0f), true),
        new CameraDefinition("DepthCam_FL", "legacy_corner_front_left", "corner_minusX_plusZ", new Vector3(-4.5f, 1.35f,  2.5f), new Vector3(0f, 0.4f, 0f), true, new Vector3(10.4626548f, 119.054611f, 0f), true),
        new CameraDefinition("DepthCam_FR", "legacy_corner_front_right", "corner_plusX_plusZ", new Vector3( 4.5f, 1.35f,  2.5f), new Vector3(0f, 0.4f, 0f), true, new Vector3(10.4626548f, 240.945389f, 0f), true),
    };

    private static readonly CameraDefinition[] Cam3Definitions =
    {
        LookAtCamera("DepthCam_0", "minusZ_high_side", "minus_z_wall", new Vector3(-1.35f, 2.30f, -2.70f), new Vector3(1.20f, 1.05f, 0f)),
        LookAtCamera("DepthCam_1", "plusZ_high_side", "plus_z_wall", new Vector3(2.65f, 2.20f, 2.70f), new Vector3(1.55f, 1.05f, 0f)),
        LookAtCamera("DepthCam_2", "foot_wall_robot_axis", "minus_x_wall", new Vector3(-4.65f, 2.05f, 0f), new Vector3(0.95f, 1.10f, 0f)),
    };

    private static readonly CameraDefinition[] Cam4AsymDefinitions =
    {
        LookAtCamera("DepthCam_0", "foot_wall_robot_axis", "minus_x_wall", new Vector3(-4.65f, 2.05f, 0f), new Vector3(0.95f, 1.10f, 0f)),
        LookAtCamera("DepthCam_1", "head_wall_anesthesia_axis", "plus_x_wall", new Vector3(4.65f, 2.05f, 0f), new Vector3(2.05f, 1.10f, 0f)),
        LookAtCamera("DepthCam_2", "minusZ_high_side_clear_tv", "minus_z_wall", new Vector3(-1.35f, 2.30f, -2.70f), new Vector3(1.20f, 1.05f, 0f)),
        LookAtCamera("DepthCam_3", "plusZ_high_side", "plus_z_wall", new Vector3(2.65f, 2.20f, 2.70f), new Vector3(1.55f, 1.05f, 0f)),
    };

    private static readonly CameraDefinition[] Cam5Definitions =
    {
        LookAtCamera("DepthCam_0", "foot_wall_robot_axis", "minus_x_wall", new Vector3(-4.65f, 2.05f, 0f), new Vector3(0.95f, 1.10f, 0f)),
        LookAtCamera("DepthCam_1", "head_wall_anesthesia_axis", "plus_x_wall", new Vector3(4.65f, 2.05f, 0f), new Vector3(2.05f, 1.10f, 0f)),
        LookAtCamera("DepthCam_2", "minusZ_high_side_clear_tv", "minus_z_wall", new Vector3(-1.35f, 2.30f, -2.70f), new Vector3(1.20f, 1.05f, 0f)),
        LookAtCamera("DepthCam_3", "plusZ_high_side", "plus_z_wall", new Vector3(2.65f, 2.20f, 2.70f), new Vector3(1.55f, 1.05f, 0f)),
        LookAtCamera("DepthCam_4", "mid_oblique_head_minusZ", "plus_x_minus_z_oblique", new Vector3(4.45f, 1.55f, -2.55f), new Vector3(1.25f, 1.05f, 0f)),
    };

    [Header("Camera Settings")]
    public ResolutionPreset resolutionPreset = ResolutionPreset.LUCID;
    public int width = 640;
    public int height = 480;
    public float nearClip = 0.3f;
    public float farClip = 8.3f;
    public float horizontalFovDegrees = 108f;
    public float verticalFovDegrees = 78f;
    public bool camerasEnabledInGameView = false;

    [SerializeField, HideInInspector] private CameraRigPreset activeCameraRig = CameraRigPreset.Cam4Classic;
    [SerializeField, HideInInspector] private string activeCameraCaptureMode = "4CamClassic";

    public CameraRigPreset ActiveCameraRig
    {
        get { return activeCameraRig; }
    }

    public string ActiveCameraRigId
    {
        get { return GetRigExportId(activeCameraRig); }
    }

    private void Start()
    {
        ApplyResolutionPreset();
        ApplyConfigurationToChildren();
    }

    private void OnValidate()
    {
        ApplyResolutionPreset();
        ApplyConfigurationToChildren();
    }

    private void ApplyResolutionPreset()
    {
        switch (resolutionPreset)
        {
            case ResolutionPreset.FullHD:
                width = 1920;
                height = 1080;
                verticalFovDegrees = 70f;
                horizontalFovDegrees = CalculateHorizontalFov(verticalFovDegrees, width, height);
                break;
            case ResolutionPreset.UHD4K:
                width = 3840;
                height = 2160;
                verticalFovDegrees = 70f;
                horizontalFovDegrees = CalculateHorizontalFov(verticalFovDegrees, width, height);
                break;
            case ResolutionPreset.LUCID:
                width = 640;
                height = 480;
                horizontalFovDegrees = 108f;
                verticalFovDegrees = 78f;
                break;
            case ResolutionPreset.Custom:
                width = Mathf.Max(1, width);
                height = Mathf.Max(1, height);
                verticalFovDegrees = Mathf.Clamp(verticalFovDegrees, 1f, 179f);
                horizontalFovDegrees = horizontalFovDegrees > 0f
                    ? Mathf.Clamp(horizontalFovDegrees, 1f, 179f)
                    : CalculateHorizontalFov(verticalFovDegrees, width, height);
                break;
        }
    }

    public Camera[] GetOrderedCameras()
    {
        List<Camera> cameras = new List<Camera>();
        CameraDefinition[] definitions = GetDefinitions(activeCameraRig);

        for (int i = 0; i < definitions.Length; i++)
        {
            Transform child = transform.Find(definitions[i].name);
            if (child == null)
            {
                continue;
            }

            Camera cameraComponent = child.GetComponent<Camera>();
            if (cameraComponent != null)
            {
                cameras.Add(cameraComponent);
            }
        }

        return cameras.ToArray();
    }

    public void ApplyCameraRigPreset(CameraRigPreset rigPreset, string cameraCaptureMode)
    {
        activeCameraRig = rigPreset;
        activeCameraCaptureMode = string.IsNullOrEmpty(cameraCaptureMode) ? GetRigExportId(rigPreset) : cameraCaptureMode;
        ApplyConfigurationToChildren();
    }

    [ContextMenu("Apply Configuration To Child Cameras")]
    public void ApplyConfigurationToChildren()
    {
        ApplyResolutionPreset();
        CameraDefinition[] definitions = GetDefinitions(activeCameraRig);

        for (int i = 0; i < definitions.Length; i++)
        {
            Transform child = GetOrCreateCameraChild(definitions[i]);
            if (child == null)
            {
                continue;
            }

            child.localPosition = definitions[i].position;
            child.localRotation = ResolveRotation(definitions[i]);

            Camera cameraComponent = child.GetComponent<Camera>();
            if (cameraComponent != null)
            {
                ConfigureCamera(cameraComponent);
            }
        }

        DisableUnusedDepthCameras(definitions);
    }

    public void ConfigureCamera(Camera cameraComponent)
    {
        if (cameraComponent == null)
        {
            return;
        }

        cameraComponent.enabled = camerasEnabledInGameView;
        cameraComponent.nearClipPlane = nearClip;
        cameraComponent.farClipPlane = farClip;
        cameraComponent.fieldOfView = verticalFovDegrees;
        cameraComponent.ResetProjectionMatrix();
        cameraComponent.projectionMatrix = BuildProjectionMatrix(nearClip, farClip, horizontalFovDegrees, verticalFovDegrees);
        cameraComponent.allowHDR = false;
        cameraComponent.allowMSAA = false;
        cameraComponent.depthTextureMode = DepthTextureMode.Depth;
    }

    public DepthMetadataExport BuildDepthMetadata(int sampleIndex, string sampleName, string timestampUtc)
    {
        DepthMetadataExport export = new DepthMetadataExport();
        export.sampleIndex = sampleIndex;
        export.sampleName = sampleName;
        export.timestampUtc = timestampUtc;
        export.cameraRigId = ActiveCameraRigId;
        export.cameraCaptureMode = activeCameraCaptureMode;
        export.usesCameraJitter = false;
        export.width = width;
        export.height = height;
        export.nearClip = nearClip;
        export.farClip = farClip;
        export.fovDegrees = verticalFovDegrees;
        export.horizontalFovDegrees = horizontalFovDegrees;
        export.verticalFovDegrees = verticalFovDegrees;

        Camera[] cameras = GetOrderedCameras();
        CameraDefinition[] definitions = GetDefinitions(activeCameraRig);
        export.numDepthCameras = cameras.Length;
        for (int i = 0; i < cameras.Length; i++)
        {
            export.fullDepthRawFiles.Add("cam" + i + "_depth.raw");
            export.fullDepthPreviewFiles.Add("cam" + i + "_depth_vis.png");
            export.cameras.Add(BuildCameraMetadata(i, cameras[i], definitions[i]));
        }

        return export;
    }

    public SensorCameraMetadata BuildMetadataForCamera(Camera cameraComponent, int index)
    {
        if (cameraComponent == null)
        {
            return null;
        }

        CameraDefinition[] definitions = GetDefinitions(activeCameraRig);
        CameraDefinition definition = index >= 0 && index < definitions.Length ? definitions[index] : BuildFallbackDefinition(cameraComponent);
        return BuildCameraMetadata(index, cameraComponent, definition);
    }

    private SensorCameraMetadata BuildCameraMetadata(int index, Camera cameraComponent, CameraDefinition definition)
    {
        SensorCameraMetadata metadata = new SensorCameraMetadata();
        metadata.index = index;
        metadata.name = cameraComponent.name;
        metadata.cameraRole = definition.role;
        metadata.mountWall = definition.mountWall;
        metadata.lookAtTarget = definition.lookAtTarget;
        metadata.downTiltDegrees = CalculateDownTilt(definition.position, definition.lookAtTarget);
        metadata.yawDegrees = CalculateYaw(definition.position, definition.lookAtTarget);
        metadata.isBaseline = definition.isBaseline;
        metadata.position = cameraComponent.transform.position;
        metadata.eulerRotation = cameraComponent.transform.eulerAngles;
        metadata.forward = cameraComponent.transform.forward;
        metadata.nearClip = cameraComponent.nearClipPlane;
        metadata.farClip = cameraComponent.farClipPlane;
        metadata.verticalFovDegrees = cameraComponent.fieldOfView;
        metadata.horizontalFovDegrees = horizontalFovDegrees;
        metadata.fy = (height * 0.5f) / Mathf.Tan(verticalFovDegrees * 0.5f * Mathf.Deg2Rad);
        metadata.fx = (width * 0.5f) / Mathf.Tan(horizontalFovDegrees * 0.5f * Mathf.Deg2Rad);
        metadata.cx = (width - 1) * 0.5f;
        metadata.cy = (height - 1) * 0.5f;
        metadata.worldToCameraMatrix = FlattenMatrix(cameraComponent.worldToCameraMatrix);
        metadata.cameraToWorldMatrix = FlattenMatrix(cameraComponent.cameraToWorldMatrix);
        metadata.projectionMatrix = FlattenMatrix(cameraComponent.projectionMatrix);
        metadata.inverseProjectionMatrix = FlattenMatrix(cameraComponent.projectionMatrix.inverse);
        return metadata;
    }

    private static CameraDefinition LookAtCamera(string name, string role, string mountWall, Vector3 position, Vector3 lookAtTarget)
    {
        return new CameraDefinition(name, role, mountWall, position, lookAtTarget, false, Vector3.zero, false);
    }

    private static CameraDefinition[] GetDefinitions(CameraRigPreset rigPreset)
    {
        switch (rigPreset)
        {
            case CameraRigPreset.Cam3:
                return Cam3Definitions;
            case CameraRigPreset.Cam4Asym:
                return Cam4AsymDefinitions;
            case CameraRigPreset.Cam5:
                return Cam5Definitions;
            case CameraRigPreset.Cam4Classic:
            default:
                return Cam4ClassicDefinitions;
        }
    }

    public static string GetRigExportId(CameraRigPreset rigPreset)
    {
        switch (rigPreset)
        {
            case CameraRigPreset.Cam3:
                return "3Cam";
            case CameraRigPreset.Cam4Asym:
                return "4CamAsym";
            case CameraRigPreset.Cam5:
                return "5Cam";
            case CameraRigPreset.Cam4Classic:
            default:
                return "4CamClassic";
        }
    }

    private Transform GetOrCreateCameraChild(CameraDefinition definition)
    {
        Transform child = transform.Find(definition.name);
        if (child != null)
        {
            return child;
        }

        if (!Application.isPlaying)
        {
            return null;
        }

        GameObject cameraObject = new GameObject(definition.name);
        cameraObject.transform.SetParent(transform, false);
        cameraObject.AddComponent<Camera>();
        return cameraObject.transform;
    }

    private void DisableUnusedDepthCameras(CameraDefinition[] activeDefinitions)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null || !child.name.StartsWith("DepthCam"))
            {
                continue;
            }

            Camera cameraComponent = child.GetComponent<Camera>();
            if (cameraComponent == null)
            {
                continue;
            }

            bool isActive = false;
            for (int j = 0; j < activeDefinitions.Length; j++)
            {
                if (child.name == activeDefinitions[j].name)
                {
                    isActive = true;
                    break;
                }
            }

            if (!isActive)
            {
                cameraComponent.enabled = false;
            }
        }
    }

    private static Quaternion ResolveRotation(CameraDefinition definition)
    {
        if (definition.useExplicitEuler)
        {
            return Quaternion.Euler(definition.eulerRotation);
        }

        Vector3 direction = definition.lookAtTarget - definition.position;
        if (direction.sqrMagnitude <= 0.000001f)
        {
            return Quaternion.identity;
        }

        return Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private static CameraDefinition BuildFallbackDefinition(Camera cameraComponent)
    {
        Vector3 position = cameraComponent != null ? cameraComponent.transform.position : Vector3.zero;
        return new CameraDefinition(
            cameraComponent != null ? cameraComponent.name : "camera",
            "",
            "",
            position,
            position + (cameraComponent != null ? cameraComponent.transform.forward : Vector3.forward),
            false,
            Vector3.zero,
            false);
    }

    private static float CalculateDownTilt(Vector3 position, Vector3 target)
    {
        Vector3 delta = target - position;
        float horizontalDistance = new Vector2(delta.x, delta.z).magnitude;
        if (horizontalDistance <= 0.0001f)
        {
            return 0f;
        }

        return Mathf.Atan2(position.y - target.y, horizontalDistance) * Mathf.Rad2Deg;
    }

    private static float CalculateYaw(Vector3 position, Vector3 target)
    {
        Vector3 delta = target - position;
        if (new Vector2(delta.x, delta.z).sqrMagnitude <= 0.000001f)
        {
            return 0f;
        }

        float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        return yaw < 0f ? yaw + 360f : yaw;
    }

    private static float CalculateHorizontalFov(float verticalFovDegrees, int width, int height)
    {
        float aspect = height > 0 ? (float)width / height : 1f;
        float verticalRadians = verticalFovDegrees * Mathf.Deg2Rad;
        return 2f * Mathf.Atan(Mathf.Tan(verticalRadians * 0.5f) * aspect) * Mathf.Rad2Deg;
    }

    private static Matrix4x4 BuildProjectionMatrix(float nearClip, float farClip, float horizontalFovDegrees, float verticalFovDegrees)
    {
        float halfWidth = Mathf.Tan(horizontalFovDegrees * 0.5f * Mathf.Deg2Rad) * nearClip;
        float halfHeight = Mathf.Tan(verticalFovDegrees * 0.5f * Mathf.Deg2Rad) * nearClip;
        return Matrix4x4.Frustum(-halfWidth, halfWidth, -halfHeight, halfHeight, nearClip, farClip);
    }

    private static List<float> FlattenMatrix(Matrix4x4 matrix)
    {
        List<float> values = new List<float>(16);
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                values.Add(matrix[row, column]);
            }
        }
        return values;
    }
}
