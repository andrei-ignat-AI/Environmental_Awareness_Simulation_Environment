using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SensorCameraMetadata
{
    public int index;
    public string name;
    public string cameraRole;
    public string mountWall;
    public Vector3 lookAtTarget;
    public float downTiltDegrees;
    public float yawDegrees;
    public bool isBaseline;
    public Vector3 position;
    public Vector3 eulerRotation;
    public Vector3 forward;
    public float nearClip;
    public float farClip;
    public float horizontalFovDegrees;
    public float verticalFovDegrees;
    public float fx;
    public float fy;
    public float cx;
    public float cy;
    public List<float> worldToCameraMatrix = new List<float>();
    public List<float> cameraToWorldMatrix = new List<float>();
    public List<float> projectionMatrix = new List<float>();
    public List<float> inverseProjectionMatrix = new List<float>();
}

[Serializable]
public class DepthMetadataExport
{
    public int sampleIndex;
    public string sampleName;
    public string timestampUtc;
    public string cameraRigId;
    public string cameraCaptureMode;
    public int numDepthCameras;
    public bool usesCameraJitter;
    public int width;
    public int height;
    public float nearClip;
    public float farClip;
    public float fovDegrees;
    public float horizontalFovDegrees;
    public float verticalFovDegrees;
    public bool depthValuesInMeters = true;
    public string pixelStorageOrder = "row-major, x-fastest, y from bottom to top (Unity GetPixels order)";
    public string matrixStorageOrder = "row-major flattened 4x4";
    public bool includesFullDepth = true;
    public bool includesRobotFreeDepth = false;
    public bool includesRobotMask = false;
    public List<string> fullDepthRawFiles = new List<string>();
    public List<string> fullDepthPreviewFiles = new List<string>();
    public List<string> robotFreeDepthRawFiles = new List<string>();
    public List<string> robotFreeDepthPreviewFiles = new List<string>();
    public List<string> robotMaskRawFiles = new List<string>();
    public List<string> robotMaskPreviewFiles = new List<string>();
    public List<SensorCameraMetadata> cameras = new List<SensorCameraMetadata>();
}
