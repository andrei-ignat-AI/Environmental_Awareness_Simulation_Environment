using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SceneObjectExport
{
    public string objectId;
    public string semanticId;
    public string category;
    public string surface;
    public Vector3 position;
    public Vector3 eulerRotation;
    public Vector3 scale;
    public Vector3 boundsSize;

    // Additive layout-debug metadata. These fields describe semantic placement intent;
    // canonical geometry remains position/eulerRotation/scale/boundsSize above.
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
public class SceneObjectsExport
{
    public List<SceneObjectExport> objects = new List<SceneObjectExport>();
}
