using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies lightweight table-aware steering and validates final robot/table
/// clearance using bounds on the lower moving chain.
/// </summary>
[DisallowMultipleComponent]
public class RobotTableAvoidance : MonoBehaviour
{
    private static readonly string[] ProtectedTableParts = { "TableTop", "Rail_Left", "Rail_Right" };
    private static readonly string[] RobotClearanceLinks = { "Sleeve", "CArc" };

    [Header("Dependencies")]
    public RobotPoseController controller;
    public GameObject tableRoot;

    [Header("Heuristic Steering")]
    [Tooltip("Extra longitudinal padding around the tabletop footprint.")]
    public float longitudinalSafetyMargin = 0.25f;
    [Tooltip("Only force side steering near the central table zone; final clearance still guards the full table.")]
    public float steeringZoneHalfLength = 0.70f;
    [Tooltip("If the gantry is over the table footprint, force at least this much side swing.")]
    public float minimumSideSwingDegrees = 70f;
    [Tooltip("When very close to table center, use a stronger side bias.")]
    public float preferredSideSwingDegrees = 105f;
    [Tooltip("Keep the support joint conservative while over the table.")]
    public float supportClampDegrees = 35f;

    [Header("Final Pose Clearance")]
    public float tableClearanceMeters = 0.05f;
    public float cArcCenterClearanceMeters = 0.20f;
    public bool logAdjustments = false;
    public bool logValidationFailures = false;

    [Header("Observed State")]
    [SerializeField] private float lastTableCenterX;
    [SerializeField] private float lastTableHalfLength;
    [SerializeField] private bool lastPoseWasOverTable;
    [SerializeField] private bool lastClearanceWasSafe;
    [SerializeField] [TextArea(2, 8)] private string lastAdjustmentSummary = "";
    [SerializeField] [TextArea(2, 8)] private string lastClearanceSummary = "";

    private void Awake()
    {
        AutoResolveDependencies();
        RefreshTableMetrics();
    }

    private void OnValidate()
    {
        AutoResolveDependencies();
        RefreshTableMetrics();
    }

    public RobotPoseTarget BuildTableAwarePose(
        RobotPoseTarget sampledPose,
        RobotPoseProfile profile,
        int seed,
        out string adjustmentSummary)
    {
        adjustmentSummary = "table_avoidance_not_used";

        AutoResolveDependencies();
        RefreshTableMetrics();

        if (sampledPose == null)
        {
            return null;
        }

        if (tableRoot == null || !TryGetLongitudinalTableZone(out float tableCenterX, out float tableHalfLength))
        {
            adjustmentSummary = "table_reference_missing";
            lastAdjustmentSummary = adjustmentSummary;
            return ClonePose(sampledPose);
        }

        RobotPoseTarget adjustedPose = ClonePose(sampledPose);

        float longTarget = GetTarget(adjustedPose, RobotArticulationJointId.Long);
        float swingTarget = GetTarget(adjustedPose, RobotArticulationJointId.Z1Rot);
        float supportTarget = GetTarget(adjustedPose, RobotArticulationJointId.Z2Rot);
        float paddedHalfLength = Mathf.Min(tableHalfLength + longitudinalSafetyMargin, steeringZoneHalfLength);
        bool overTable = Mathf.Abs(longTarget - tableCenterX) <= paddedHalfLength;
        bool nearTableCenter = Mathf.Abs(longTarget - tableCenterX) <= Mathf.Max(0.15f, tableHalfLength * 0.35f);
        lastPoseWasOverTable = overTable;

        if (!overTable)
        {
            adjustmentSummary = "clear_of_table_zone";
            lastAdjustmentSummary = adjustmentSummary;
            return adjustedPose;
        }

        float preferredSign = ResolvePreferredSideSign(profile, swingTarget, seed);
        float requiredSwing = nearTableCenter ? preferredSideSwingDegrees : minimumSideSwingDegrees;
        float adjustedSwing = Mathf.Abs(swingTarget) < requiredSwing
            ? preferredSign * requiredSwing
            : swingTarget;

        float adjustedSupport = Mathf.Abs(supportTarget) < (supportClampDegrees * 0.75f)
            ? preferredSign * supportClampDegrees
            : Mathf.Sign(supportTarget) * Mathf.Clamp(Mathf.Abs(supportTarget), 0f, supportClampDegrees);

        SetTarget(adjustedPose, RobotArticulationJointId.Z1Rot, adjustedSwing);
        SetTarget(adjustedPose, RobotArticulationJointId.Z2Rot, adjustedSupport);

        adjustmentSummary =
            "over_table_zone" +
            " long=" + longTarget.ToString("F2") +
            " center=" + tableCenterX.ToString("F2") +
            " swing=" + swingTarget.ToString("F1") + "->" + adjustedSwing.ToString("F1") +
            " support=" + supportTarget.ToString("F1") + "->" + adjustedSupport.ToString("F1");

        lastAdjustmentSummary = adjustmentSummary;
        if (logAdjustments)
        {
            Debug.Log("RobotTableAvoidance: " + adjustmentSummary);
        }

        return adjustedPose;
    }

    public bool ValidateCurrentPoseClearance(out string clearanceSummary)
    {
        AutoResolveDependencies();
        clearanceSummary = "clearance_check_not_run";

        if (controller == null || controller.robotRoot == null)
        {
            clearanceSummary = "robot_reference_missing";
            lastClearanceSummary = clearanceSummary;
            lastClearanceWasSafe = false;
            return false;
        }

        if (tableRoot == null)
        {
            clearanceSummary = "table_reference_missing";
            lastClearanceSummary = clearanceSummary;
            lastClearanceWasSafe = false;
            return false;
        }

        Bounds protectedTableBounds;
        if (!TryGetCombinedBoundsByName(tableRoot.transform, ProtectedTableParts, out protectedTableBounds))
        {
            clearanceSummary = "protected_table_bounds_missing";
            lastClearanceSummary = clearanceSummary;
            lastClearanceWasSafe = false;
            return false;
        }

        Bounds lowerRobotBounds;
        if (!TryGetCombinedBoundsByName(controller.robotRoot.transform, RobotClearanceLinks, out lowerRobotBounds))
        {
            clearanceSummary = "robot_clearance_bounds_missing";
            lastClearanceSummary = clearanceSummary;
            lastClearanceWasSafe = false;
            return false;
        }

        Bounds expandedTableBounds = protectedTableBounds;
        expandedTableBounds.Expand(tableClearanceMeters * 2f);

        bool isSafe = !expandedTableBounds.Intersects(lowerRobotBounds);

        Transform cArcTransform;
        if (controller.TryGetJointTransform("CArc", out cArcTransform))
        {
            float cArcCenterDistance = Mathf.Sqrt(expandedTableBounds.SqrDistance(cArcTransform.position));
            if (cArcCenterDistance < cArcCenterClearanceMeters)
            {
                isSafe = false;
            }
        }

        lastClearanceWasSafe = isSafe;
        clearanceSummary =
            "tableClearance=" + isSafe +
            " tableCenter=" + expandedTableBounds.center.ToString("F3") +
            " robotCenter=" + lowerRobotBounds.center.ToString("F3");
        lastClearanceSummary = clearanceSummary;

        if (!isSafe && logValidationFailures)
        {
            Debug.LogWarning("RobotTableAvoidance: " + clearanceSummary);
        }

        return isSafe;
    }

    private bool TryGetLongitudinalTableZone(out float centerX, out float halfLength)
    {
        centerX = 0f;
        halfLength = 0.5f;

        if (tableRoot == null)
        {
            return false;
        }

        SurgeryTableBuilder table = tableRoot.GetComponent<SurgeryTableBuilder>();
        if (table == null)
        {
            return false;
        }

        centerX = tableRoot.transform.position.x + table.longitudinalOffset;
        halfLength = table.topLength * 0.5f;
        return true;
    }

    private void RefreshTableMetrics()
    {
        if (TryGetLongitudinalTableZone(out float centerX, out float halfLength))
        {
            lastTableCenterX = centerX;
            lastTableHalfLength = halfLength;
        }
    }

    private void AutoResolveDependencies()
    {
        if (controller == null)
        {
            controller = GetComponent<RobotPoseController>();
            if (controller == null)
            {
                controller = FindAnyObjectByType<RobotPoseController>();
            }
        }

        if (tableRoot == null)
        {
            SurgeryTableBuilder table = FindAnyObjectByType<SurgeryTableBuilder>();
            if (table != null)
            {
                tableRoot = table.gameObject;
            }
        }
    }

    private static bool TryGetCombinedBoundsByName(Transform root, string[] names, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;

        if (root == null || names == null)
        {
            return false;
        }

        for (int i = 0; i < names.Length; i++)
        {
            Transform child = FindNamedTransform(root, names[i]);
            if (child == null)
            {
                continue;
            }

            if (!TryGetSubtreeBounds(child, out Bounds childBounds))
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = childBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(childBounds);
            }
        }

        return hasBounds;
    }

    private static bool TryGetSubtreeBounds(Transform target, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        return TryCollectSubtreeBounds(target, target, ref bounds);
    }

    private static Transform FindNamedTransform(Transform root, string targetName)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == targetName)
            {
                return transforms[i];
            }
        }

        return null;
    }

    private static bool TryCollectSubtreeBounds(Transform root, Transform current, ref Bounds bounds)
    {
        if (current != root && current.GetComponent<ArticulationBody>() != null)
        {
            return false;
        }

        bool hasBounds = false;

        Collider[] colliders = current.GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (!hasBounds)
            {
                bounds = colliders[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(colliders[i].bounds);
            }
        }

        Renderer[] renderers = current.GetComponents<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!hasBounds)
            {
                bounds = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        for (int i = 0; i < current.childCount; i++)
        {
            Bounds childBounds = new Bounds(Vector3.zero, Vector3.zero);
            if (!TryCollectSubtreeBounds(root, current.GetChild(i), ref childBounds))
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = childBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(childBounds);
            }
        }

        return hasBounds;
    }

    private static RobotPoseTarget ClonePose(RobotPoseTarget source)
    {
        RobotPoseTarget clone = new RobotPoseTarget();
        clone.profileName = source.profileName;
        clone.seed = source.seed;

        for (int i = 0; i < source.joints.Count; i++)
        {
            RobotJointCommand original = source.joints[i];
            clone.joints.Add(new RobotJointCommand
            {
                jointName = original.jointName,
                semanticRole = original.semanticRole,
                unit = original.unit,
                target = original.target
            });
        }

        return clone;
    }

    private float ResolvePreferredSideSign(RobotPoseProfile profile, float swingTarget, int seed)
    {
        switch (profile)
        {
            case RobotPoseProfile.AroundTableLeft:
                return 1f;
            case RobotPoseProfile.AroundTableRight:
                return -1f;
            default:
                if (Mathf.Abs(swingTarget) > 1f)
                {
                    return Mathf.Sign(swingTarget);
                }

                System.Random random = new System.Random(seed);
                return random.NextDouble() >= 0.5d ? 1f : -1f;
        }
    }

    private static float GetTarget(RobotPoseTarget poseTarget, RobotArticulationJointId jointId)
    {
        if (!TryFindCommand(poseTarget, jointId, out RobotJointCommand command))
        {
            return 0f;
        }

        return command.target;
    }

    private static void SetTarget(RobotPoseTarget poseTarget, RobotArticulationJointId jointId, float value)
    {
        if (TryFindCommand(poseTarget, jointId, out RobotJointCommand command))
        {
            command.target = value;
        }
    }

    private static bool TryFindCommand(RobotPoseTarget poseTarget, RobotArticulationJointId jointId, out RobotJointCommand command)
    {
        IReadOnlyList<RobotPoseController.JointDefinition> definitions = RobotPoseController.JointDefinitions;
        string jointName = null;

        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i].jointId == jointId)
            {
                jointName = definitions[i].jointName;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(jointName))
        {
            command = null;
            return false;
        }

        for (int i = 0; i < poseTarget.joints.Count; i++)
        {
            if (poseTarget.joints[i].jointName == jointName)
            {
                command = poseTarget.joints[i];
                return true;
            }
        }

        command = null;
        return false;
    }
}
