using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Samples curated robot poses without applying them directly.
/// This keeps motion sampling separate from articulation control and export.
/// </summary>
[DisallowMultipleComponent]
public class RobotPoseRandomizer : MonoBehaviour
{
    [Serializable]
    private struct JointRange
    {
        public float min;
        public float max;

        public JointRange(float min, float max)
        {
            this.min = min;
            this.max = max;
        }
    }

    [Header("Sampling")]
    public int previewSeed = 1000;
    public RobotPoseProfile previewProfile = RobotPoseProfile.FreeRoomSnapshot;
    public bool logSampledPose = false;

    public RobotPoseTarget SamplePose(RobotPoseProfile profile, int seed)
    {
        System.Random random = new System.Random(seed);
        Dictionary<RobotArticulationJointId, float> sampledValues = BuildSampledValues(profile, seed, random);

        RobotPoseTarget target = new RobotPoseTarget();
        target.profileName = profile.ToString();
        target.seed = seed;

        IReadOnlyList<RobotPoseController.JointDefinition> definitions = RobotPoseController.JointDefinitions;
        for (int i = 0; i < definitions.Count; i++)
        {
            RobotPoseController.JointDefinition definition = definitions[i];
            float sampled;
            if (!sampledValues.TryGetValue(definition.jointId, out sampled))
            {
                sampled = 0f;
            }

            target.joints.Add(new RobotJointCommand
            {
                jointName = definition.jointName,
                semanticRole = definition.semanticRole.ToString(),
                unit = definition.exportUnit == RobotJointUnit.Meters ? "meters" : "degrees",
                target = sampled
            });
        }

        if (logSampledPose)
        {
            Debug.Log("RobotPoseRandomizer: Sampled " + target.profileName + " with seed " + seed + ".");
        }

        return target;
    }

    [ContextMenu("Log Preview Pose")]
    public void LogPreviewPose()
    {
        RobotPoseTarget target = SamplePose(previewProfile, previewSeed);
        Debug.Log(JsonUtility.ToJson(target, true));
    }

    private static Dictionary<RobotArticulationJointId, float> BuildSampledValues(RobotPoseProfile profile, int seed, System.Random random)
    {
        Dictionary<RobotArticulationJointId, float> values = CreateParkedValues();

        if (profile == RobotPoseProfile.Parked)
        {
            return values;
        }

        float sidePreference = ResolveSidePreference(profile, random);
        values[RobotArticulationJointId.Long] = SampleRange(BuildLongitudinalRange(profile), random);

        float sampledSwing = SampleSwingRange(profile, sidePreference, random);
        values[RobotArticulationJointId.Z1Rot] = sampledSwing;

        values[RobotArticulationJointId.Z2Rot] = SampleSignedMagnitude(
            sidePreference,
            new JointRange(35f, 85f),
            random,
            35f);
        values[RobotArticulationJointId.Prop] = SampleRangeWithMinimumMagnitude(
            new JointRange(-70f, 70f),
            random,
            15f);
        values[RobotArticulationJointId.CArc] = SampleRangeWithMinimumMagnitude(
            new JointRange(-55f, 55f),
            random,
            8f);

        return values;
    }

    private static JointRange BuildLongitudinalRange(RobotPoseProfile profile)
    {
        switch (profile)
        {
            case RobotPoseProfile.AroundTableLeft:
            case RobotPoseProfile.AroundTableRight:
                return new JointRange(1.20f, 2.00f);
            case RobotPoseProfile.CranialCaudal:
                return new JointRange(0.90f, 1.60f);
            case RobotPoseProfile.FreeRoomSnapshot:
            default:
                return new JointRange(0.45f, 1.85f);
        }
    }

    private static float SampleSwingRange(RobotPoseProfile profile, float sidePreference, System.Random random)
    {
        switch (profile)
        {
            case RobotPoseProfile.AroundTableLeft:
                return SampleRange(new JointRange(85f, 145f), random);
            case RobotPoseProfile.AroundTableRight:
                return SampleRange(new JointRange(-145f, -85f), random);
            case RobotPoseProfile.CranialCaudal:
                return SampleSignedMagnitude(sidePreference, new JointRange(45f, 110f), random, 45f);
            case RobotPoseProfile.FreeRoomSnapshot:
            default:
                return SampleSignedMagnitude(sidePreference, new JointRange(55f, 145f), random, 55f);
        }
    }

    private static float ResolveSidePreference(RobotPoseProfile profile, System.Random random)
    {
        switch (profile)
        {
            case RobotPoseProfile.AroundTableLeft:
                return 1f;
            case RobotPoseProfile.AroundTableRight:
                return -1f;
            default:
                return random.NextDouble() >= 0.5d ? 1f : -1f;
        }
    }

    private static float SampleSignedMagnitude(float sidePreference, JointRange range, System.Random random, float minimumMagnitude)
    {
        float magnitude;
        if (range.min >= 0f)
        {
            magnitude = SampleRange(range, random);
        }
        else
        {
            magnitude = Mathf.Abs(SampleRangeWithMinimumMagnitude(range, random, minimumMagnitude));
        }

        return sidePreference >= 0f ? magnitude : -magnitude;
    }

    private static Dictionary<RobotArticulationJointId, float> CreateParkedValues()
    {
        Dictionary<RobotArticulationJointId, float> values = new Dictionary<RobotArticulationJointId, float>();
        values[RobotArticulationJointId.Long] = 0f;
        values[RobotArticulationJointId.Z1Rot] = 0f;
        values[RobotArticulationJointId.Z2Rot] = 0f;
        values[RobotArticulationJointId.Prop] = 0f;
        values[RobotArticulationJointId.CArc] = 0f;
        return values;
    }

    private static float SampleRange(JointRange range, System.Random random)
    {
        if (Mathf.Approximately(range.min, range.max))
        {
            return range.min;
        }

        return Mathf.Lerp(range.min, range.max, (float)random.NextDouble());
    }

    private static float SampleRangeWithMinimumMagnitude(JointRange range, System.Random random, float minimumMagnitude)
    {
        float sampled = SampleRange(range, random);
        if (minimumMagnitude <= 0f)
        {
            return sampled;
        }

        if (range.min >= 0f || range.max <= 0f)
        {
            return sampled;
        }

        if (Mathf.Abs(sampled) >= minimumMagnitude)
        {
            return sampled;
        }

        float availablePositive = Mathf.Max(0f, range.max - minimumMagnitude);
        float availableNegative = Mathf.Max(0f, -range.min - minimumMagnitude);

        if (availablePositive <= 0f && availableNegative <= 0f)
        {
            return sampled;
        }

        bool choosePositive;
        if (availablePositive > 0f && availableNegative > 0f)
        {
            choosePositive = random.NextDouble() >= 0.5d;
        }
        else
        {
            choosePositive = availablePositive > 0f;
        }

        if (choosePositive)
        {
            return Mathf.Lerp(minimumMagnitude, range.max, (float)random.NextDouble());
        }

        return Mathf.Lerp(range.min, -minimumMagnitude, (float)random.NextDouble());
    }
}
