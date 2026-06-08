using System;
using System.Collections.Generic;
using UnityEngine;

public enum SimplePropSurface
{
    Floor,
    Ceiling,
    Table,
    Wall
}

public enum SimplePropLayoutRole
{
    None,
    PatientOnTable,
    OperatingDoctor,
    MedicalCart,
    AnesthesiaDoctor,
    AnesthesiaMachine,
    RadiationShield,
    TvScreen,
    WasteBin,
    UltrasoundDoctor,
    MobileEchoCart,
    CeilingLamp
}

public enum SimplePropPlacementMode
{
    AnchoredWorld,
    FixedWorld
}

[Serializable]
public class SimplePropSpec
{
    public string id;
    public SimplePropSurface surface;
    public Vector3 minScale = Vector3.one;
    public Vector3 maxScale = Vector3.one;
    public GameObject prefab;
    public bool required = true;
    public SimplePropLayoutRole layoutRole;
    public SimplePropPlacementMode placementMode = SimplePropPlacementMode.AnchoredWorld;
    public Vector3 anchorWorldPosition;
    public Vector3 anchorWorldEulerAngles;
    public float floorYOffset;
    public bool useImportedFloorAxis;

    [Header("Temporary Primitive Exception")]
    public bool allowPrimitiveFallback;
    public PrimitiveType primitiveType = PrimitiveType.Cylinder;
    public Color primitiveColor = Color.gray;

    [Header("Resolved Semantic Layout")]
    public string clinicalConfig;
    public string layoutFamily;
    public int primaryOperatorSideZ;
    public string semanticZone;
    public Vector3 targetBoundsMeters;
    public Vector3 sampledJitterMeters;
    public float sampledYawJitterDegrees;
    public string associatedWith;
}

[Serializable]
public class SimpleSceneSummary
{
    public int sampleIndex = -1;
    public int robotSeed = -1;
    public bool accepted;
    public string status = "";
    public string rejectionReason = "";
    public string clinicalConfig = "";
    public string layoutFamily = "";
    public int primaryOperatorSideZ;
    public int requestedProps;
    public int placedProps;
    public int rejectedProps;
    public List<string> placedPropIds = new List<string>();
}

/// <summary>
/// Runtime-only semantic placement metadata exported additively for layout debugging.
/// These fields complement canonical transform/bounds data; downstream readers should not depend on them for geometry.
/// </summary>
public class SimplePropMetadata : MonoBehaviour
{
    public string clinicalConfig;
    public string layoutFamily;
    public int primaryOperatorSideZ;
    public string semanticZone;
    public Vector3 targetBoundsMeters;
    public Vector3 sampledJitterMeters;
    public float sampledYawJitterDegrees;
    public string associatedWith;
}
