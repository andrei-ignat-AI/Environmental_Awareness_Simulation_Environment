using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SimplePropLibrary : MonoBehaviour
{
    public List<SimplePropSpec> specs = new List<SimplePropSpec>();

    public IReadOnlyList<SimplePropSpec> Specs
    {
        get { return specs; }
    }

    public bool TryBuildDeterministicSceneSet(out List<SimplePropSpec> sceneSet)
    {
        return TryBuildDeterministicSceneSet(0, out sceneSet);
    }

    public bool TryBuildDeterministicSceneSet(int sampleIndex, out List<SimplePropSpec> sceneSet)
    {
        sceneSet = new List<SimplePropSpec>();
        if (specs == null || specs.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < specs.Count; i++)
        {
            SimplePropSpec spec = specs[i];
            if (spec == null)
            {
                continue;
            }

            sceneSet.Add(CloneSpec(spec));
        }

        if (sceneSet.Count <= 0)
        {
            return false;
        }

        SimpleSemanticLayoutPlanner.ApplyLayout(sceneSet, sampleIndex);
        return true;
    }

    private static SimplePropSpec CloneSpec(SimplePropSpec source)
    {
        if (source == null)
        {
            return null;
        }

        return new SimplePropSpec
        {
            id = source.id,
            surface = source.surface,
            minScale = source.minScale,
            maxScale = source.maxScale,
            prefab = source.prefab,
            required = source.required,
            layoutRole = source.layoutRole,
            placementMode = source.placementMode,
            anchorWorldPosition = source.anchorWorldPosition,
            anchorWorldEulerAngles = source.anchorWorldEulerAngles,
            floorYOffset = source.floorYOffset,
            useImportedFloorAxis = source.useImportedFloorAxis,
            allowPrimitiveFallback = source.allowPrimitiveFallback,
            primitiveType = source.primitiveType,
            primitiveColor = source.primitiveColor,
            clinicalConfig = source.clinicalConfig,
            layoutFamily = source.layoutFamily,
            primaryOperatorSideZ = source.primaryOperatorSideZ,
            semanticZone = source.semanticZone,
            targetBoundsMeters = source.targetBoundsMeters,
            sampledJitterMeters = source.sampledJitterMeters,
            sampledYawJitterDegrees = source.sampledYawJitterDegrees,
            associatedWith = source.associatedWith,
        };
    }
}

public static class SimpleSemanticLayoutPlanner
{
    private const float SmallEpsilon = 0.0001f;

    private struct LayoutDefinition
    {
        public readonly string clinicalConfig;
        public readonly string layoutFamily;
        public readonly int primarySideZ;
        public readonly Vector3 patient;
        public readonly Vector3 doctorOperator;
        public readonly Vector3 doctorAnesthesia;
        public readonly Vector3 doctorUltrasound;
        public readonly Vector3 medicalTrolley;
        public readonly Vector3 ultrasound;
        public readonly Vector3 anesthesiaMachine;
        public readonly Vector3 radiationShield;
        public readonly Vector3 uhdTv;
        public readonly Vector3 wasteBin;
        public readonly Vector3 ceilingLamp;

        public LayoutDefinition(
            string clinicalConfig,
            string layoutFamily,
            int primarySideZ,
            Vector3 patient,
            Vector3 doctorOperator,
            Vector3 doctorAnesthesia,
            Vector3 doctorUltrasound,
            Vector3 medicalTrolley,
            Vector3 ultrasound,
            Vector3 anesthesiaMachine,
            Vector3 radiationShield,
            Vector3 uhdTv,
            Vector3 wasteBin,
            Vector3 ceilingLamp)
        {
            this.clinicalConfig = clinicalConfig;
            this.layoutFamily = layoutFamily;
            this.primarySideZ = primarySideZ;
            this.patient = patient;
            this.doctorOperator = doctorOperator;
            this.doctorAnesthesia = doctorAnesthesia;
            this.doctorUltrasound = doctorUltrasound;
            this.medicalTrolley = medicalTrolley;
            this.ultrasound = ultrasound;
            this.anesthesiaMachine = anesthesiaMachine;
            this.radiationShield = radiationShield;
            this.uhdTv = uhdTv;
            this.wasteBin = wasteBin;
            this.ceilingLamp = ceilingLamp;
        }
    }

    public static void ApplyLayout(List<SimplePropSpec> specs, int sampleIndex)
    {
        if (specs == null)
        {
            return;
        }

        LayoutDefinition layout = ResolveLayout(sampleIndex);
        for (int i = 0; i < specs.Count; i++)
        {
            ApplySpecLayout(specs[i], layout, sampleIndex);
        }
    }

    private static LayoutDefinition ResolveLayout(int sampleIndex)
    {
        switch (PositiveModulo(sampleIndex, 4))
        {
            case 1:
                return new LayoutDefinition(
                    "neuro",
                    "N1_mirrored_head",
                    1,
                    new Vector3(0.752f, 0.896f, -0.12f),
                    new Vector3(1.85f, 0f, 1.55f),
                    new Vector3(2.35f, 0f, -1.25f),
                    new Vector3(-3.25f, 0f, -1.65f),
                    new Vector3(-0.55f, 0f, 2.15f),
                    new Vector3(-3.85f, 0f, -1.35f),
                    new Vector3(3.35f, 0f, -1.70f),
                    new Vector3(1.65f, 0f, 1.05f),
                    new Vector3(1.45f, 1.75f, 2.00f),
                    new Vector3(4.45f, 0f, 2.45f),
                    new Vector3(1.85f, 0f, 0.55f));
            case 2:
                return new LayoutDefinition(
                    "cardio",
                    "C2_radial_echo",
                    -1,
                    new Vector3(0.752f, 0.896f, -0.12f),
                    new Vector3(1.45f, 0f, -1.60f),
                    new Vector3(2.35f, 0f, 1.25f),
                    new Vector3(-3.55f, 0f, 1.85f),
                    new Vector3(-0.65f, 0f, -2.20f),
                    new Vector3(-3.85f, 0f, 1.80f),
                    new Vector3(3.35f, 0f, 1.70f),
                    new Vector3(1.55f, 0f, -1.05f),
                    new Vector3(1.55f, 1.75f, -2.00f),
                    new Vector3(4.35f, 0f, -2.35f),
                    new Vector3(1.75f, 0f, -0.60f));
            case 3:
                return new LayoutDefinition(
                    "neuro",
                    "N2_mirrored_support",
                    1,
                    new Vector3(0.752f, 0.896f, -0.12f),
                    new Vector3(2.25f, 0f, 1.60f),
                    new Vector3(2.35f, 0f, -1.25f),
                    new Vector3(-2.85f, 0f, -1.90f),
                    new Vector3(-0.65f, 0f, 2.20f),
                    new Vector3(-3.65f, 0f, -1.85f),
                    new Vector3(3.35f, 0f, -1.70f),
                    new Vector3(2.05f, 0f, 1.05f),
                    new Vector3(2.00f, 1.75f, 2.00f),
                    new Vector3(4.40f, 0f, 2.40f),
                    new Vector3(2.10f, 0f, 0.60f));
            case 0:
            default:
                return new LayoutDefinition(
                    "cardio",
                    "C1_standard_cath",
                    -1,
                    new Vector3(0.752f, 0.896f, -0.12f),
                    new Vector3(0.75f, 0f, -1.55f),
                    new Vector3(2.35f, 0f, 1.25f),
                    new Vector3(-3.25f, 0f, 1.65f),
                    new Vector3(-0.55f, 0f, -2.15f),
                    new Vector3(-3.85f, 0f, 1.35f),
                    new Vector3(3.35f, 0f, 1.70f),
                    new Vector3(0.95f, 0f, -1.05f),
                    new Vector3(1.15f, 1.75f, -2.00f),
                    new Vector3(4.45f, 0f, -2.45f),
                    new Vector3(1.35f, 0f, -0.55f));
        }
    }

    private static void ApplySpecLayout(SimplePropSpec spec, LayoutDefinition layout, int sampleIndex)
    {
        if (spec == null)
        {
            return;
        }

        Vector3 anchor;
        Vector3 euler;
        Vector3 positionJitter;
        float yawJitter;
        Vector3 targetBounds;
        string semanticZone;
        string associatedWith;

        ResolveSpecDefaults(spec, layout, out anchor, out euler, out positionJitter, out yawJitter, out targetBounds, out semanticZone, out associatedWith);

        System.Random random = new System.Random(StableSeed(sampleIndex, spec.id));
        Vector3 sampledJitter = new Vector3(
            RandomRange(random, -positionJitter.x, positionJitter.x),
            RandomRange(random, -positionJitter.y, positionJitter.y),
            RandomRange(random, -positionJitter.z, positionJitter.z));
        float sampledYaw = RandomRange(random, -yawJitter, yawJitter);

        spec.anchorWorldPosition = anchor + sampledJitter;
        spec.anchorWorldEulerAngles = euler + new Vector3(0f, sampledYaw, 0f);
        spec.targetBoundsMeters = targetBounds;
        spec.sampledJitterMeters = sampledJitter;
        spec.sampledYawJitterDegrees = sampledYaw;
        spec.clinicalConfig = layout.clinicalConfig;
        spec.layoutFamily = layout.layoutFamily;
        spec.primaryOperatorSideZ = layout.primarySideZ;
        spec.semanticZone = semanticZone;
        spec.associatedWith = associatedWith;
    }

    private static void ResolveSpecDefaults(
        SimplePropSpec spec,
        LayoutDefinition layout,
        out Vector3 anchor,
        out Vector3 euler,
        out Vector3 positionJitter,
        out float yawJitter,
        out Vector3 targetBounds,
        out string semanticZone,
        out string associatedWith)
    {
        anchor = spec.anchorWorldPosition;
        euler = spec.anchorWorldEulerAngles;
        positionJitter = Vector3.zero;
        yawJitter = 0f;
        targetBounds = Vector3.zero;
        semanticZone = "";
        associatedWith = "";

        switch (spec.id)
        {
            case "patient":
                anchor = layout.patient;
                euler = new Vector3(0f, 90f, 0f);
                positionJitter = new Vector3(0.03f, 0f, 0.02f);
                yawJitter = 2f;
                semanticZone = "table_patient";
                break;
            case "doctor_operator":
                anchor = layout.doctorOperator;
                euler = new Vector3(0f, layout.primarySideZ < 0 ? 0f : 180f, 0f);
                positionJitter = new Vector3(0.15f, 0f, 0.12f);
                yawJitter = 15f;
                semanticZone = "operator_lane";
                associatedWith = "radiation_shield";
                break;
            case "doctor_anesthesia":
                anchor = layout.doctorAnesthesia;
                euler = new Vector3(0f, layout.primarySideZ < 0 ? -45f : 45f, 0f);
                positionJitter = new Vector3(0.15f, 0f, 0.12f);
                yawJitter = 15f;
                semanticZone = "head_anesthesia_corner";
                associatedWith = "anesthesia_machine";
                break;
            case "doctor_ultrasound":
                anchor = layout.doctorUltrasound;
                euler = new Vector3(0f, layout.primarySideZ < 0 ? 45f : -45f, 0f);
                positionJitter = new Vector3(0.15f, 0f, 0.12f);
                yawJitter = 15f;
                semanticZone = "peripheral_support";
                associatedWith = "ultrasound";
                break;
            case "medical_trolley":
                anchor = layout.medicalTrolley;
                euler = new Vector3(-90f, layout.primarySideZ < 0 ? 0f : 180f, 0f);
                positionJitter = new Vector3(0.08f, 0f, 0.08f);
                yawJitter = 20f;
                targetBounds = new Vector3(0.85f, 0.82f, 0.55f);
                semanticZone = "operator_peripheral_equipment";
                associatedWith = "doctor_operator";
                break;
            case "ultrasound":
                anchor = layout.ultrasound;
                euler = new Vector3(-90f, layout.primarySideZ < 0 ? 90f : -90f, 0f);
                positionJitter = new Vector3(0.22f, 0f, 0.18f);
                yawJitter = 20f;
                targetBounds = new Vector3(0.90f, 1.30f, 0.59f);
                semanticZone = "peripheral_support";
                associatedWith = "doctor_ultrasound";
                break;
            case "anesthesia_machine":
                anchor = layout.anesthesiaMachine;
                euler = new Vector3(-90f, layout.primarySideZ < 0 ? -90f : 90f, 0f);
                positionJitter = new Vector3(0.18f, 0f, 0.15f);
                yawJitter = 15f;
                targetBounds = new Vector3(1.15f, 1.48f, 0.79f);
                semanticZone = "head_anesthesia_corner";
                associatedWith = "doctor_anesthesia";
                break;
            case "radiation_shield":
                anchor = layout.radiationShield;
                euler = new Vector3(0f, layout.primarySideZ < 0 ? -90f : 90f, 0f);
                positionJitter = new Vector3(0.14f, 0f, 0.05f);
                yawJitter = 10f;
                targetBounds = new Vector3(0.80f, 1.90f, 0.42f);
                semanticZone = "operator_lane";
                associatedWith = "doctor_operator";
                break;
            case "uhd_tv":
                anchor = new Vector3(1.15f, 1.75f, -2.00f);
                euler = new Vector3(-90f, 0f, -90f);
                positionJitter = Vector3.zero;
                yawJitter = 0f;
                targetBounds = new Vector3(1.45f, 0.88f, 0.22f);
                semanticZone = "screen_side";
                associatedWith = "doctor_operator";
                break;
            case "waste_bin":
                anchor = layout.wasteBin;
                euler = new Vector3(0f, layout.primarySideZ < 0 ? -135f : 135f, 0f);
                positionJitter = new Vector3(0.25f, 0f, 0.25f);
                yawJitter = 180f;
                targetBounds = new Vector3(0.44f, 0.78f, 0.44f);
                semanticZone = "waste_corner";
                break;
            case "ceiling_lamp":
                anchor = layout.ceilingLamp;
                euler = Vector3.zero;
                positionJitter = new Vector3(0.15f, 0f, 0.10f);
                yawJitter = 0f;
                targetBounds = new Vector3(0.62f, 0.16f, 0.62f);
                semanticZone = "ceiling_over_table";
                break;
        }
    }

    private static int PositiveModulo(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static int StableSeed(int sampleIndex, string id)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + sampleIndex;
            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < id.Length; i++)
                {
                    hash = (hash * 31) + id[i];
                }
            }

            return hash;
        }
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        if (Mathf.Abs(max - min) <= SmallEpsilon)
        {
            return min;
        }

        return Mathf.Lerp(min, max, (float)random.NextDouble());
    }
}
