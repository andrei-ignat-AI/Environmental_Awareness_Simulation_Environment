using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single authority for reading and writing robot articulation pose targets.
/// This stays separate from export/capture logic so later sample generation
/// can depend on one consistent motion API.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-400)]
public class RobotPoseController : MonoBehaviour
{
    [Serializable]
    public class JointDefinition
    {
        public RobotArticulationJointId jointId;
        public string jointName;
        public string childLinkName;
        public string legacyName;
        public string linkPath;
        public string jointType;
        public RobotJointSemanticRole semanticRole;
        public RobotJointUnit driveUnit;
        public RobotJointUnit exportUnit;
        public float conservativeMin;
        public float conservativeMax;

        public JointDefinition(
            RobotArticulationJointId jointId,
            string jointName,
            string childLinkName,
            string legacyName,
            string linkPath,
            string jointType,
            RobotJointSemanticRole semanticRole,
            RobotJointUnit driveUnit,
            RobotJointUnit exportUnit,
            float conservativeMin,
            float conservativeMax)
        {
            this.jointId = jointId;
            this.jointName = jointName;
            this.childLinkName = childLinkName;
            this.legacyName = legacyName;
            this.linkPath = linkPath;
            this.jointType = jointType;
            this.semanticRole = semanticRole;
            this.driveUnit = driveUnit;
            this.exportUnit = exportUnit;
            this.conservativeMin = conservativeMin;
            this.conservativeMax = conservativeMax;
        }
    }

    private static readonly JointDefinition[] Definitions =
    {
        new JointDefinition(RobotArticulationJointId.Long, "Long", "Carriage", "Carriage", "FlexArm/Room/Rail/Carriage", "PrismaticJoint", RobotJointSemanticRole.RailLongitudinal, RobotJointUnit.Meters, RobotJointUnit.Meters, -1f, 1f),
        new JointDefinition(RobotArticulationJointId.Z1Rot, "Z1Rot", "HorBeam", "HorBeam", "FlexArm/Room/Rail/Carriage/HorBeam", "RevoluteJoint", RobotJointSemanticRole.FlexArmSwing, RobotJointUnit.Degrees, RobotJointUnit.Degrees, -90f, 90f),
        new JointDefinition(RobotArticulationJointId.Z2Rot, "Z2Rot", "VerBeam", "VerBeam", "FlexArm/Room/Rail/Carriage/HorBeam/VerBeam", "RevoluteJoint", RobotJointSemanticRole.SupportParallelToFloor, RobotJointUnit.Degrees, RobotJointUnit.Degrees, -45f, 45f),
        new JointDefinition(RobotArticulationJointId.Prop, "Prop", "Sleeve", "Sleeve", "FlexArm/Room/Rail/Carriage/HorBeam/VerBeam/Sleeve", "RevoluteJoint", RobotJointSemanticRole.CArmAngulation, RobotJointUnit.Degrees, RobotJointUnit.Degrees, -45f, 45f),
        new JointDefinition(RobotArticulationJointId.CArc, "CArc", "CArc", "CArc", "FlexArm/Room/Rail/Carriage/HorBeam/VerBeam/Sleeve/CArc", "RevoluteJoint", RobotJointSemanticRole.CArmInPlaneRotation, RobotJointUnit.Degrees, RobotJointUnit.Degrees, -90f, 90f),
    };

    [Header("Robot Reference")]
    [Tooltip("Assign the FlexArm root object.")]
    public GameObject robotRoot;

    [Header("Debug")]
    public bool logPoseApplications = true;
    public bool logInitializationWarnings = true;

    private readonly Dictionary<string, ArticulationBody> bodiesByJointName = new Dictionary<string, ArticulationBody>();
    private readonly Dictionary<string, float> lastAppliedTargets = new Dictionary<string, float>();
    private readonly Dictionary<string, float> lastAppliedDriveTargets = new Dictionary<string, float>();

    private bool initialized;

    public static IReadOnlyList<JointDefinition> JointDefinitions
    {
        get { return Definitions; }
    }

    public bool IsInitialized
    {
        get { return initialized; }
    }

    private void Start()
    {
        EnsureInitialized();
    }

    [ContextMenu("Initialize Robot Pose Controller")]
    public void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        bodiesByJointName.Clear();

        if (robotRoot == null)
        {
            if (logInitializationWarnings)
            {
                Debug.LogError("RobotPoseController: Assign robotRoot (FlexArm).");
            }
            return;
        }

        ArticulationBody[] allBodies = robotRoot.GetComponentsInChildren<ArticulationBody>(true);
        for (int i = 0; i < Definitions.Length; i++)
        {
            JointDefinition definition = Definitions[i];
            ArticulationBody match = FindBodyByName(allBodies, definition.childLinkName);
            if (match == null)
            {
                if (logInitializationWarnings)
                {
                    Debug.LogWarning("RobotPoseController: Missing articulation body for " + definition.childLinkName);
                }
                continue;
            }

            bodiesByJointName[definition.jointName] = match;
            if (!lastAppliedTargets.ContainsKey(definition.jointName))
            {
                lastAppliedTargets[definition.jointName] = match.xDrive.target;
            }
        }

        initialized = bodiesByJointName.Count == Definitions.Length;
        if (initialized)
        {
            Debug.Log("RobotPoseController: Initialized with " + bodiesByJointName.Count + " movable joints.");
        }
    }

    public RobotPoseTarget BuildParkedPose()
    {
        RobotPoseTarget target = new RobotPoseTarget();
        target.profileName = RobotPoseProfile.Parked.ToString();
        target.seed = 0;

        for (int i = 0; i < Definitions.Length; i++)
        {
            target.joints.Add(new RobotJointCommand
            {
                jointName = Definitions[i].jointName,
                semanticRole = Definitions[i].semanticRole.ToString(),
                unit = GetUnitLabel(Definitions[i].exportUnit),
                target = 0f
            });
        }

        return target;
    }

    [ContextMenu("Apply Parked Pose")]
    public void ApplyParkedPose()
    {
        string error;
        if (!TryApplyPoseTarget(BuildParkedPose(), out error))
        {
            Debug.LogError("RobotPoseController: " + error);
        }
    }

    public bool TryApplyPoseTarget(RobotPoseTarget poseTarget, out string error)
    {
        error = null;

        if (poseTarget == null)
        {
            error = "Pose target is null.";
            return false;
        }

        EnsureInitialized();
        if (!initialized)
        {
            error = "Controller is not initialized.";
            return false;
        }

        for (int i = 0; i < poseTarget.joints.Count; i++)
        {
            RobotJointCommand command = poseTarget.joints[i];
            ArticulationBody body;
            if (!bodiesByJointName.TryGetValue(command.jointName, out body))
            {
                error = "Unknown joint target: " + command.jointName;
                return false;
            }

            JointDefinition definition;
            if (!TryGetJointDefinition(command.jointName, out definition))
            {
                error = "Missing joint definition for " + command.jointName;
                return false;
            }

            RobotJointUnit commandUnit = ParseCommandUnit(command.unit, definition.exportUnit);
            float driveTarget = ConvertValue(command.target, commandUnit, definition.driveUnit);
            ArticulationDrive drive = body.xDrive;
            drive.target = driveTarget;
            body.xDrive = drive;
            lastAppliedTargets[command.jointName] = command.target;
            lastAppliedDriveTargets[command.jointName] = driveTarget;
        }

        if (logPoseApplications)
        {
            Debug.Log("RobotPoseController: Applied profile " + poseTarget.profileName + " with seed " + poseTarget.seed + ".");
        }

        return true;
    }

    public bool TryGetMeasuredJointPosition(string jointName, out float value)
    {
        value = 0f;

        EnsureInitialized();
        if (!initialized)
        {
            return false;
        }

        ArticulationBody body;
        if (!bodiesByJointName.TryGetValue(jointName, out body))
        {
            return false;
        }

        JointDefinition definition;
        if (!TryGetJointDefinition(jointName, out definition))
        {
            return false;
        }

        value = ConvertValue(ReadReducedCoordinate(body.jointPosition), definition.driveUnit, definition.exportUnit);
        return true;
    }

    public bool TryGetDriveTarget(string jointName, out float driveTarget)
    {
        driveTarget = 0f;

        EnsureInitialized();
        if (!initialized)
        {
            return false;
        }

        ArticulationBody body;
        if (!bodiesByJointName.TryGetValue(jointName, out body))
        {
            return false;
        }

        driveTarget = body.xDrive.target;
        return true;
    }

    public bool TryGetJointTransform(string jointName, out Transform jointTransform)
    {
        jointTransform = null;

        EnsureInitialized();
        if (!initialized)
        {
            return false;
        }

        ArticulationBody body;
        if (!bodiesByJointName.TryGetValue(jointName, out body))
        {
            return false;
        }

        jointTransform = body.transform;
        return true;
    }

    public bool TryGetJointDefinition(string jointName, out JointDefinition definition)
    {
        for (int i = 0; i < Definitions.Length; i++)
        {
            if (Definitions[i].jointName == jointName)
            {
                definition = Definitions[i];
                return true;
            }
        }

        definition = null;
        return false;
    }

    public bool TryGetJointDefinition(RobotArticulationJointId jointId, out JointDefinition definition)
    {
        for (int i = 0; i < Definitions.Length; i++)
        {
            if (Definitions[i].jointId == jointId)
            {
                definition = Definitions[i];
                return true;
            }
        }

        definition = null;
        return false;
    }

    public CanonicalRobotStateExport CaptureCanonicalStateExport()
    {
        EnsureInitialized();

        CanonicalRobotStateExport export = new CanonicalRobotStateExport();
        export.rootName = robotRoot != null ? robotRoot.name : "FlexArm";
        export.rootPosition = robotRoot != null ? robotRoot.transform.position : Vector3.zero;
        export.rootEulerRotation = robotRoot != null ? robotRoot.transform.eulerAngles : Vector3.zero;

        for (int i = 0; i < Definitions.Length; i++)
        {
            JointDefinition definition = Definitions[i];
            ArticulationBody body;
            if (!bodiesByJointName.TryGetValue(definition.jointName, out body))
            {
                continue;
            }

            CanonicalRobotJointExport joint = new CanonicalRobotJointExport();
            joint.jointName = definition.jointName;
            joint.childLinkName = definition.childLinkName;
            joint.legacyName = definition.legacyName;
            joint.linkPath = definition.linkPath;
            joint.jointType = definition.jointType;
            joint.semanticRole = definition.semanticRole.ToString();
            joint.driveUnit = GetUnitLabel(definition.driveUnit);
            joint.unit = GetUnitLabel(definition.exportUnit);
            joint.conservativeMin = definition.conservativeMin;
            joint.conservativeMax = definition.conservativeMax;
            joint.sampledTarget = GetLastAppliedTarget(definition.jointName);
            joint.driveTarget = body.xDrive.target;
            joint.jointPosition = ConvertValue(ReadReducedCoordinate(body.jointPosition), definition.driveUnit, definition.exportUnit);
            joint.jointVelocity = ConvertValue(ReadReducedCoordinate(body.jointVelocity), definition.driveUnit, definition.exportUnit);
            joint.worldPosition = body.transform.position;
            joint.worldEulerRotation = body.transform.eulerAngles;
            export.joints.Add(joint);
        }

        return export;
    }

    [ContextMenu("Log Canonical Robot State")]
    public void LogCanonicalRobotState()
    {
        CanonicalRobotStateExport export = CaptureCanonicalStateExport();
        Debug.Log(JsonUtility.ToJson(export, true));
    }

    private float GetLastAppliedTarget(string jointName)
    {
        float value;
        if (lastAppliedTargets.TryGetValue(jointName, out value))
        {
            return value;
        }
        return 0f;
    }

    public float GetLastAppliedDriveTarget(string jointName)
    {
        float value;
        if (lastAppliedDriveTargets.TryGetValue(jointName, out value))
        {
            return value;
        }
        return 0f;
    }

    private static float ReadReducedCoordinate(ArticulationReducedSpace reducedSpace)
    {
        return reducedSpace.dofCount > 0 ? reducedSpace[0] : 0f;
    }

    private static RobotJointUnit ParseCommandUnit(string unit, RobotJointUnit fallback)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return fallback;
        }

        string normalized = unit.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "meters":
            case "meter":
            case "m":
                return RobotJointUnit.Meters;
            case "degrees":
            case "degree":
            case "deg":
                return RobotJointUnit.Degrees;
            case "radians":
            case "radian":
            case "rad":
                return RobotJointUnit.Radians;
            default:
                return fallback;
        }
    }

    private static string GetUnitLabel(RobotJointUnit unit)
    {
        switch (unit)
        {
            case RobotJointUnit.Meters:
                return "meters";
            case RobotJointUnit.Degrees:
                return "degrees";
            case RobotJointUnit.Radians:
            default:
                return "radians";
        }
    }

    private static float ConvertValue(float value, RobotJointUnit from, RobotJointUnit to)
    {
        if (from == to)
        {
            return value;
        }

        if (from == RobotJointUnit.Degrees && to == RobotJointUnit.Radians)
        {
            return value * Mathf.Deg2Rad;
        }

        if (from == RobotJointUnit.Radians && to == RobotJointUnit.Degrees)
        {
            return value * Mathf.Rad2Deg;
        }

        return value;
    }

    private static ArticulationBody FindBodyByName(ArticulationBody[] bodies, string bodyName)
    {
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] != null && bodies[i].gameObject.name == bodyName)
            {
                return bodies[i];
            }
        }

        return null;
    }
}
