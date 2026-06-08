using System;
using System.Collections.Generic;
using UnityEngine;

public enum RobotPoseProfile
{
    Parked,
    AroundTableLeft,
    AroundTableRight,
    CranialCaudal,
    FreeRoomSnapshot
}

public enum RobotArticulationJointId
{
    Long,
    Z1Rot,
    Z2Rot,
    Prop,
    CArc
}

public enum RobotJointSemanticRole
{
    RailLongitudinal,
    FlexArmSwing,
    SupportParallelToFloor,
    CArmAngulation,
    CArmInPlaneRotation
}

public enum RobotJointUnit
{
    Meters,
    Degrees,
    Radians
}

[Serializable]
public class RobotJointCommand
{
    public string jointName;
    public string semanticRole;
    public string unit;
    public float target;
}

[Serializable]
public class RobotPoseTarget
{
    public string profileName;
    public int seed;
    public List<RobotJointCommand> joints = new List<RobotJointCommand>();
}

[Serializable]
public class RobotJointValidation
{
    public string jointName;
    public float target;
    public float measured;
    public float absoluteError;
    public bool withinTolerance;
}

[Serializable]
public class RobotPoseValidationReport
{
    public string profileName;
    public bool isValid;
    public int matchedJointCount;
    public float maxError;
    public List<RobotJointValidation> joints = new List<RobotJointValidation>();
}

[Serializable]
public class CanonicalRobotJointExport
{
    public string jointName;
    public string semanticRole;
    public string childLinkName;
    public string legacyName;
    public string linkPath;
    public string jointType;
    public string driveUnit;
    public string unit;
    public float conservativeMin;
    public float conservativeMax;
    public float sampledTarget;
    public float driveTarget;
    public float jointPosition;
    public float jointVelocity;
    public Vector3 worldPosition;
    public Vector3 worldEulerRotation;
}

[Serializable]
public class CanonicalRobotStateExport
{
    public string rootName;
    public Vector3 rootPosition;
    public Vector3 rootEulerRotation;
    public List<CanonicalRobotJointExport> joints = new List<CanonicalRobotJointExport>();
}
