using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Validates whether measured articulation state converged to the requested pose.
/// Collision-aware validation can be added later without changing the caller API.
/// </summary>
[DisallowMultipleComponent]
public class RobotPoseValidator : MonoBehaviour
{
    [Header("Dependencies")]
    public RobotPoseController controller;

    [Header("Tolerance")]
    [Tooltip("Prismatic-joint tolerance in meters.")]
    public float prismaticToleranceMeters = 0.01f;
    [Tooltip("Revolute-joint tolerance in degrees.")]
    public float revoluteToleranceDegrees = 2f;

    [Header("Runtime Validation")]
    public float validationTimeoutSeconds = 0.6f;

    public RobotPoseValidationReport Validate(RobotPoseTarget poseTarget)
    {
        RobotPoseValidationReport report = new RobotPoseValidationReport();
        report.profileName = poseTarget != null ? poseTarget.profileName : "Unknown";
        report.isValid = false;
        report.maxError = 0f;

        if (controller == null || poseTarget == null)
        {
            return report;
        }

        bool allWithinTolerance = true;
        for (int i = 0; i < poseTarget.joints.Count; i++)
        {
            RobotJointCommand command = poseTarget.joints[i];
            float measured;
            if (!controller.TryGetMeasuredJointPosition(command.jointName, out measured))
            {
                allWithinTolerance = false;
                continue;
            }

            float tolerance = GetTolerance(command.jointName);
            float error = Mathf.Abs(measured - command.target);
            if (error > report.maxError)
            {
                report.maxError = error;
            }

            RobotJointValidation jointReport = new RobotJointValidation();
            jointReport.jointName = command.jointName;
            jointReport.target = command.target;
            jointReport.measured = measured;
            jointReport.absoluteError = error;
            jointReport.withinTolerance = error <= tolerance;
            report.joints.Add(jointReport);
            report.matchedJointCount++;

            if (!jointReport.withinTolerance)
            {
                allWithinTolerance = false;
            }
        }

        report.isValid = allWithinTolerance && report.matchedJointCount == poseTarget.joints.Count;
        return report;
    }

    public IEnumerator WaitForValidation(
        RobotPoseTarget poseTarget,
        Action<RobotPoseValidationReport> onComplete)
    {
        float deadline = Time.time + validationTimeoutSeconds;
        RobotPoseValidationReport report = Validate(poseTarget);

        while (!report.isValid && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            report = Validate(poseTarget);
        }

        onComplete?.Invoke(report);
    }

    [ContextMenu("Log Current Validation")]
    public void LogCurrentValidation()
    {
        if (controller == null)
        {
            Debug.LogWarning("RobotPoseValidator: Assign controller first.");
            return;
        }

        RobotPoseTarget parked = controller.BuildParkedPose();
        RobotPoseValidationReport report = Validate(parked);
        Debug.Log(JsonUtility.ToJson(report, true));
    }

    private float GetTolerance(string jointName)
    {
        RobotPoseController.JointDefinition definition;
        if (controller != null && controller.TryGetJointDefinition(jointName, out definition))
        {
            return definition.exportUnit == RobotJointUnit.Meters
                ? prismaticToleranceMeters
                : revoluteToleranceDegrees;
        }

        return revoluteToleranceDegrees;
    }
}
