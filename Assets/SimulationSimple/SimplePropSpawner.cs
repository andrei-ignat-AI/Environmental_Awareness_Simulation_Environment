using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SimplePropSpawner : MonoBehaviour
{
    private const string ActiveRootName = "ActiveSimpleProps";
    private const string CandidateRootName = "CandidateSimpleProps";
    private const string GeneratedColliderRootName = "GeneratedVisualColliders";

    private readonly List<Collider> tempColliders = new List<Collider>();
    private readonly List<Collider> robotColliders = new List<Collider>();
    private readonly List<Collider> forbiddenColliders = new List<Collider>();
    private readonly List<Collider> existingCandidateColliders = new List<Collider>();
    private readonly Dictionary<Color, Material> materialsByColor = new Dictionary<Color, Material>();

    [Header("Dependencies")]
    public SimplePropLibrary library;
    public SimpleRobotProxyRig robotProxyRig;
    public SimpleForbiddenZones forbiddenZones;

    [Header("Placement")]
    public float supportSurfaceClearance = 0.01f;

    [Header("State")]
    public Transform activePropsRoot;
    public Transform candidatePropsRoot;
    [SerializeField] [TextArea(4, 10)] private string lastSummaryJson = "";

    public SimpleSceneSummary LastSummary { get; private set; }

    private void Awake()
    {
        EnsureRoots();
    }

    private void OnValidate()
    {
        EnsureRoots();
    }

    public bool TrySpawnScene(int sampleIndex, int robotSeed, out SimpleSceneSummary summary)
    {
        if (!TryPrepareCandidateScene(sampleIndex, robotSeed, out summary))
        {
            return false;
        }

        if (!ValidateRobotAgainstCandidateProps(out string overlapReason))
        {
            summary.rejectionReason = overlapReason;
            ClearRoot(candidatePropsRoot);
            RecordSummary(summary);
            return false;
        }

        CommitCandidateScene(summary);
        return true;
    }

    public bool TryPrepareCandidateScene(int sampleIndex, int robotSeed, out SimpleSceneSummary summary)
    {
        EnsureRoots();
        ClearRoot(candidatePropsRoot);

        summary = new SimpleSceneSummary
        {
            sampleIndex = sampleIndex,
            robotSeed = robotSeed,
            status = "rejected",
        };

        if (library == null || robotProxyRig == null || forbiddenZones == null)
        {
            summary.rejectionReason = "dependencies_missing";
            RecordSummary(summary);
            return false;
        }

        List<SimplePropSpec> sceneSet;
        if (!library.TryBuildDeterministicSceneSet(sampleIndex, out sceneSet))
        {
            summary.rejectionReason = "failed_to_build_scene_set";
            RecordSummary(summary);
            return false;
        }

        ApplyLayoutSummary(summary, sceneSet);
        summary.requestedProps = sceneSet.Count;
        for (int propIndex = 0; propIndex < sceneSet.Count; propIndex++)
        {
            SimplePropSpec spec = sceneSet[propIndex];
            GameObject candidate = BuildPropObject(spec, ResolveScale(spec));
            if (candidate == null)
            {
                summary.rejectedProps++;
                if (spec == null || spec.required)
                {
                    summary.rejectionReason = "required_prop_failed prop=" + ResolvePropId(spec) + " reason=prefab_missing";
                    ClearRoot(candidatePropsRoot);
                    RecordSummary(summary);
                    return false;
                }

                continue;
            }

            candidate.name = "Prop_" + propIndex.ToString("D2") + "_" + ResolvePropId(spec);
            candidate.transform.SetParent(candidatePropsRoot, false);
            candidate.transform.rotation = ResolveRotation(spec);
            ApplyTargetBoundsScale(candidate, spec);
            EnsureColliderFromRenderers(candidate);

            Vector3 position;
            if (!TryResolvePosition(candidate, spec, out position))
            {
                DestroyProp(candidate);
                summary.rejectedProps++;
                if (spec == null || spec.required)
                {
                    summary.rejectionReason = "required_prop_failed prop=" + ResolvePropId(spec) + " reason=position_unavailable";
                    ClearRoot(candidatePropsRoot);
                    RecordSummary(summary);
                    return false;
                }

                continue;
            }

            candidate.transform.position = position;
            EnsureFlexVisionMount(candidate, spec);
            EnsureColliderFromRenderers(candidate);

            string validationReason;
            if (!IsValidCandidate(candidate, spec, out validationReason))
            {
                string recoveryReason;
                if (TryRecoverCandidate(candidate, spec, out recoveryReason))
                {
                    validationReason = recoveryReason;
                }
                else
                {
                    DestroyProp(candidate);
                    summary.rejectedProps++;
                    if (spec == null || spec.required)
                    {
                        summary.rejectionReason = "required_prop_failed prop=" + ResolvePropId(spec) + " reason=" + validationReason;
                        ClearRoot(candidatePropsRoot);
                        RecordSummary(summary);
                        return false;
                    }

                    continue;
                }
            }

            AttachMetadata(candidate, spec);
            summary.placedProps++;
            summary.placedPropIds.Add(ResolvePropId(spec));
        }

        if (summary.placedProps <= 0)
        {
            summary.rejectionReason = "no_props_placed";
            ClearRoot(candidatePropsRoot);
            RecordSummary(summary);
            return false;
        }

        summary.status = "candidate";
        RecordSummary(summary);
        return true;
    }

    private static void ApplyLayoutSummary(SimpleSceneSummary summary, IReadOnlyList<SimplePropSpec> sceneSet)
    {
        if (summary == null || sceneSet == null)
        {
            return;
        }

        for (int i = 0; i < sceneSet.Count; i++)
        {
            SimplePropSpec spec = sceneSet[i];
            if (spec == null || string.IsNullOrEmpty(spec.layoutFamily))
            {
                continue;
            }

            summary.clinicalConfig = spec.clinicalConfig;
            summary.layoutFamily = spec.layoutFamily;
            summary.primaryOperatorSideZ = spec.primaryOperatorSideZ;
            return;
        }
    }

    public void CommitCandidateScene(SimpleSceneSummary summary)
    {
        EnsureRoots();
        ClearRoot(activePropsRoot);
        while (candidatePropsRoot.childCount > 0)
        {
            candidatePropsRoot.GetChild(0).SetParent(activePropsRoot, true);
        }

        if (summary != null)
        {
            summary.accepted = true;
            summary.status = "accepted";
            RecordSummary(summary);
        }
    }

    public void DiscardCandidateScene()
    {
        EnsureRoots();
        ClearRoot(candidatePropsRoot);
    }

    public void ClearAllProps()
    {
        EnsureRoots();
        ClearRoot(activePropsRoot);
        ClearRoot(candidatePropsRoot);
    }

    public bool ValidateRobotAgainstActiveProps(out string summary)
    {
        summary = "robot_props_clear";
        if (robotProxyRig == null)
        {
            summary = "robot_proxies_missing";
            return false;
        }

        robotProxyRig.CollectColliders(robotColliders);
        ColliderOverlapUtility.CollectColliders(activePropsRoot, tempColliders);
        return ValidateNoOverlap(robotColliders, tempColliders, "robot_prop_overlap", out summary);
    }

    public bool ValidateRobotAgainstCandidateProps(out string reason)
    {
        reason = "robot_props_clear";
        if (robotProxyRig == null)
        {
            reason = "robot_proxies_missing";
            return false;
        }

        robotProxyRig.CollectColliders(robotColliders);
        ColliderOverlapUtility.CollectColliders(candidatePropsRoot, tempColliders);
        return ValidateNoOverlap(robotColliders, tempColliders, "robot_prop_overlap", out reason);
    }

    private void EnsureRoots()
    {
        if (activePropsRoot == null)
        {
            activePropsRoot = EnsureChildRoot(ActiveRootName);
        }

        if (candidatePropsRoot == null)
        {
            candidatePropsRoot = EnsureChildRoot(CandidateRootName);
        }
    }

    private Transform EnsureChildRoot(string rootName)
    {
        Transform existing = transform.Find(rootName);
        if (existing != null)
        {
            return existing;
        }

        GameObject root = new GameObject(rootName);
        root.transform.SetParent(transform, false);
        return root.transform;
    }

    private GameObject BuildPropObject(SimplePropSpec spec, Vector3 scale)
    {
        if (spec == null)
        {
            return null;
        }

        GameObject instance;
        if (spec.prefab != null)
        {
            instance = Instantiate(spec.prefab);
        }
        else if (spec.allowPrimitiveFallback)
        {
            instance = GameObject.CreatePrimitive(spec.primitiveType);
            Renderer renderer = instance.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetMaterial(spec.primitiveColor);
            }

            Rigidbody rb = instance.GetComponent<Rigidbody>();
            if (rb != null)
            {
                DestroyImmediate(rb);
            }
        }
        else
        {
            return null;
        }

        instance.transform.localScale = scale;
        return instance;
    }

    private static void ApplyTargetBoundsScale(GameObject candidate, SimplePropSpec spec)
    {
        if (candidate == null || spec == null || spec.targetBoundsMeters.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        Bounds bounds;
        if (!TryGetRendererBounds(candidate.transform, out bounds))
        {
            return;
        }

        Vector3 worldAxisFactor = new Vector3(
            bounds.size.x > 0.0001f ? spec.targetBoundsMeters.x / bounds.size.x : 1f,
            bounds.size.y > 0.0001f ? spec.targetBoundsMeters.y / bounds.size.y : 1f,
            bounds.size.z > 0.0001f ? spec.targetBoundsMeters.z / bounds.size.z : 1f);
        Vector3 localAxisFactor = ResolveLocalAxisScaleFactor(candidate.transform, worldAxisFactor);

        Vector3 scale = candidate.transform.localScale;
        candidate.transform.localScale = new Vector3(
            Mathf.Max(0.0001f, scale.x * localAxisFactor.x),
            Mathf.Max(0.0001f, scale.y * localAxisFactor.y),
            Mathf.Max(0.0001f, scale.z * localAxisFactor.z));
        Physics.SyncTransforms();
    }

    private static Vector3 ResolveLocalAxisScaleFactor(Transform root, Vector3 worldAxisFactor)
    {
        if (root == null)
        {
            return worldAxisFactor;
        }

        return new Vector3(
            SelectDominantWorldAxisFactor(root.TransformDirection(Vector3.right), worldAxisFactor),
            SelectDominantWorldAxisFactor(root.TransformDirection(Vector3.up), worldAxisFactor),
            SelectDominantWorldAxisFactor(root.TransformDirection(Vector3.forward), worldAxisFactor));
    }

    private static float SelectDominantWorldAxisFactor(Vector3 localAxisInWorld, Vector3 worldAxisFactor)
    {
        Vector3 absoluteAxis = new Vector3(
            Mathf.Abs(localAxisInWorld.x),
            Mathf.Abs(localAxisInWorld.y),
            Mathf.Abs(localAxisInWorld.z));

        if (absoluteAxis.x >= absoluteAxis.y && absoluteAxis.x >= absoluteAxis.z)
        {
            return worldAxisFactor.x;
        }

        if (absoluteAxis.y >= absoluteAxis.x && absoluteAxis.y >= absoluteAxis.z)
        {
            return worldAxisFactor.y;
        }

        return worldAxisFactor.z;
    }

    private bool TryResolvePosition(GameObject candidate, SimplePropSpec spec, out Vector3 position)
    {
        position = Vector3.zero;
        if (candidate == null || spec == null)
        {
            return false;
        }

        if (spec.placementMode == SimplePropPlacementMode.FixedWorld)
        {
            position = spec.anchorWorldPosition;
            return true;
        }

        ColliderOverlapUtility.CollectColliders(candidate.transform, tempColliders);
        Bounds candidateBounds;
        if (!ColliderOverlapUtility.TryGetCombinedBounds(tempColliders, out candidateBounds))
        {
            return false;
        }

        Vector3 anchor = spec.anchorWorldPosition;
        switch (spec.surface)
        {
            case SimplePropSurface.Ceiling:
                float ceilingY;
                if (!forbiddenZones.TryGetCeilingY(out ceilingY))
                {
                    return false;
                }
                position = new Vector3(anchor.x, ceilingY - candidateBounds.max.y, anchor.z);
                return true;

            case SimplePropSurface.Table:
                Bounds tableSurface;
                if (!forbiddenZones.TryGetTableSurfaceBounds(out tableSurface))
                {
                    return false;
                }
                position = new Vector3(
                    anchor.x,
                    tableSurface.center.y - candidateBounds.min.y + supportSurfaceClearance + spec.floorYOffset,
                    anchor.z);
                return true;

            case SimplePropSurface.Wall:
                position = anchor;
                return true;

            case SimplePropSurface.Floor:
            default:
                position = new Vector3(
                    anchor.x,
                    supportSurfaceClearance - candidateBounds.min.y + spec.floorYOffset,
                    anchor.z);
                return true;
        }
    }

    private bool IsValidCandidate(GameObject candidate, SimplePropSpec spec, out string reason)
    {
        reason = "ok";
        ColliderOverlapUtility.CollectColliders(candidate.transform, tempColliders);
        if (tempColliders.Count == 0)
        {
            reason = "no_candidate_colliders";
            return false;
        }

        Bounds roomBounds;
        Bounds candidateBounds;
        if (!forbiddenZones.TryGetRoomInteriorBounds(out roomBounds) ||
            !ColliderOverlapUtility.TryGetCombinedBounds(tempColliders, out candidateBounds))
        {
            reason = "bounds_unavailable";
            return false;
        }

        Bounds allowedRoomBounds = roomBounds;
        if (spec != null && (spec.surface == SimplePropSurface.Ceiling || spec.surface == SimplePropSurface.Wall))
        {
            allowedRoomBounds.Expand(new Vector3(0.04f, 0.08f, 0.04f));
        }

        bool enforceFullRoomBounds = spec == null || (spec.surface != SimplePropSurface.Ceiling && spec.surface != SimplePropSurface.Wall);
        if (enforceFullRoomBounds && (!allowedRoomBounds.Contains(candidateBounds.min) || !allowedRoomBounds.Contains(candidateBounds.max)))
        {
            reason = "outside_room_bounds";
            return false;
        }

        if (!IsValidSemanticPlacement(candidateBounds, spec, out reason))
        {
            return false;
        }

        // Table props intentionally sit inside table forbidden zones; robot/table safety is checked separately.
        if (spec == null || spec.surface != SimplePropSurface.Table)
        {
            forbiddenZones.CollectAllForbiddenColliders(forbiddenColliders);
            Collider forbiddenCandidate;
            Collider forbiddenZone;
            float forbiddenDistance;
            if (ColliderOverlapUtility.TryFindOverlap(tempColliders, forbiddenColliders, out forbiddenCandidate, out forbiddenZone, out forbiddenDistance))
            {
                reason = "forbidden_overlap candidate=" + forbiddenCandidate.transform.name + " zone=" + forbiddenZone.transform.name;
                return false;
            }
        }

        ColliderOverlapUtility.CollectColliders(candidatePropsRoot, existingCandidateColliders, candidate.transform);
        Collider a;
        Collider b;
        float distance;
        if (TryFindDisallowedPropOverlap(tempColliders, existingCandidateColliders, spec, out a, out b, out distance))
        {
            reason =
                "existing_prop_overlap candidate=" + a.transform.name +
                " prop=" + b.transform.name +
                " penetration=" + distance.ToString("F4");
            return false;
        }

        return true;
    }

    private static bool TryFindDisallowedPropOverlap(
        IReadOnlyList<Collider> candidateColliders,
        IReadOnlyList<Collider> existingColliders,
        SimplePropSpec spec,
        out Collider candidateCollider,
        out Collider existingCollider,
        out float penetrationDistance)
    {
        candidateCollider = null;
        existingCollider = null;
        penetrationDistance = 0f;

        if (candidateColliders == null || existingColliders == null)
        {
            return false;
        }

        Physics.SyncTransforms();
        for (int i = 0; i < candidateColliders.Count; i++)
        {
            Collider a = candidateColliders[i];
            if (a == null || !a.enabled)
            {
                continue;
            }

            for (int j = 0; j < existingColliders.Count; j++)
            {
                Collider b = existingColliders[j];
                if (b == null || !b.enabled || a == b)
                {
                    continue;
                }

                if (!a.bounds.Intersects(b.bounds))
                {
                    continue;
                }

                Vector3 direction;
                float distance;
                if (!Physics.ComputePenetration(
                        a,
                        a.transform.position,
                        a.transform.rotation,
                        b,
                        b.transform.position,
                        b.transform.rotation,
                        out direction,
                        out distance))
                {
                    continue;
                }

                if (IsAllowedAssociatedOverlap(spec, b))
                {
                    continue;
                }

                candidateCollider = a;
                existingCollider = b;
                penetrationDistance = distance;
                return true;
            }
        }

        return false;
    }

    private static bool IsAllowedAssociatedOverlap(SimplePropSpec candidateSpec, Collider existingCollider)
    {
        if (candidateSpec == null || existingCollider == null)
        {
            return false;
        }

        string existingPropId = ExtractPropIdFromRoot(existingCollider.transform);
        return
            (candidateSpec.id == "radiation_shield" && existingPropId == "doctor_operator") ||
            (candidateSpec.id == "anesthesia_machine" && existingPropId == "doctor_anesthesia") ||
            (candidateSpec.id == "ultrasound" && existingPropId == "doctor_ultrasound") ||
            (candidateSpec.id == "uhd_tv" && existingPropId == "doctor_operator");
    }

    private static string ExtractPropIdFromRoot(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            string name = current.name;
            if (name.StartsWith("Prop_"))
            {
                int first = name.IndexOf('_');
                int second = first >= 0 ? name.IndexOf('_', first + 1) : -1;
                if (second >= 0 && second + 1 < name.Length)
                {
                    return name.Substring(second + 1);
                }
            }

            current = current.parent;
        }

        return "";
    }

    private bool TryRecoverCandidate(GameObject candidate, SimplePropSpec spec, out string reason)
    {
        reason = "ok";
        if (candidate == null || spec == null || spec.id != "radiation_shield")
        {
            return false;
        }

        float sideSign = spec.primaryOperatorSideZ == 0 ? Mathf.Sign(candidate.transform.position.z) : spec.primaryOperatorSideZ;
        if (Mathf.Approximately(sideSign, 0f))
        {
            sideSign = 1f;
        }

        candidate.transform.position += new Vector3(0f, 0f, sideSign * 0.25f);
        EnsureColliderFromRenderers(candidate);
        return IsValidCandidate(candidate, spec, out reason);
    }

    private static bool IsValidSemanticPlacement(Bounds candidateBounds, SimplePropSpec spec, out string reason)
    {
        reason = "ok";
        if (spec == null)
        {
            return true;
        }

        Vector3 center = candidateBounds.center;
        if (IsImportantForVisibility(spec.id) && Mathf.Abs(center.x) > 4.35f && Mathf.Abs(center.z) > 2.35f)
        {
            reason = "deep_corner_visibility_soft_reject";
            return false;
        }

        if (IsMobileMachineOrCart(spec.id) && IntersectsXZ(candidateBounds, 0f, 2.80f, -1.55f, 1.55f))
        {
            reason = "mobile_equipment_in_central_work_zone";
            return false;
        }

        if (IsMobileMachineOrCart(spec.id) && IntersectsXZ(candidateBounds, 2.60f, 3.30f, -0.60f, 0.60f))
        {
            reason = "mobile_equipment_in_head_airway_corridor";
            return false;
        }

        if (IsHeavyEquipment(spec.id) && IntersectsXZ(candidateBounds, -0.35f, 3.35f, -0.85f, 0.85f))
        {
            reason = "heavy_equipment_in_robot_core";
            return false;
        }

        return true;
    }

    private static bool IntersectsXZ(Bounds bounds, float minX, float maxX, float minZ, float maxZ)
    {
        return bounds.max.x >= minX && bounds.min.x <= maxX && bounds.max.z >= minZ && bounds.min.z <= maxZ;
    }

    private static bool IsImportantForVisibility(string id)
    {
        return id == "doctor_operator" ||
               id == "doctor_anesthesia" ||
               id == "doctor_ultrasound" ||
               id == "radiation_shield" ||
               id == "anesthesia_machine" ||
               id == "ultrasound" ||
               id == "uhd_tv";
    }

    private static bool IsMobileMachineOrCart(string id)
    {
        return id == "medical_trolley" || id == "ultrasound";
    }

    private static bool IsHeavyEquipment(string id)
    {
        return id == "medical_trolley" || id == "ultrasound" || id == "anesthesia_machine";
    }

    private static void AttachMetadata(GameObject candidate, SimplePropSpec spec)
    {
        if (candidate == null || spec == null)
        {
            return;
        }

        SimplePropMetadata metadata = candidate.GetComponent<SimplePropMetadata>();
        if (metadata == null)
        {
            metadata = candidate.AddComponent<SimplePropMetadata>();
        }

        metadata.clinicalConfig = spec.clinicalConfig;
        metadata.layoutFamily = spec.layoutFamily;
        metadata.primaryOperatorSideZ = spec.primaryOperatorSideZ;
        metadata.semanticZone = spec.semanticZone;
        metadata.targetBoundsMeters = spec.targetBoundsMeters;
        metadata.sampledJitterMeters = spec.sampledJitterMeters;
        metadata.sampledYawJitterDegrees = spec.sampledYawJitterDegrees;
        metadata.associatedWith = spec.associatedWith;
    }

    private bool ValidateNoOverlap(IReadOnlyList<Collider> first, IReadOnlyList<Collider> second, string prefix, out string summary)
    {
        Collider a;
        Collider b;
        float distance;
        if (ColliderOverlapUtility.TryFindOverlap(first, second, out a, out b, out distance))
        {
            summary =
                prefix +
                " a=" + a.transform.name +
                " b=" + b.transform.name +
                " penetration=" + distance.ToString("F4");
            return false;
        }

        summary = "robot_props_clear";
        return true;
    }

    private void EnsureFlexVisionMount(GameObject root, SimplePropSpec spec)
    {
        if (root == null || spec == null || spec.id != "uhd_tv")
        {
            return;
        }

        Transform existing = root.transform.Find("FlexVisionCeilingMount");
        if (existing != null)
        {
            DestroyProp(existing.gameObject);
        }

        Bounds displayBounds;
        if (!TryGetRendererBounds(root.transform, out displayBounds))
        {
            return;
        }

        float ceilingY;
        if (forbiddenZones == null || !forbiddenZones.TryGetCeilingY(out ceilingY))
        {
            ceilingY = Mathf.Max(displayBounds.max.y + 0.65f, 2.6f);
        }

        GameObject mountRoot = new GameObject("FlexVisionCeilingMount");
        mountRoot.transform.SetParent(root.transform, false);

        Material mountMaterial = GetMaterial(new Color(0.18f, 0.19f, 0.20f, 1f));
        Vector3 mountCenter = displayBounds.center;
        float dropTop = ceilingY - 0.04f;
        float dropBottom = displayBounds.max.y + 0.10f;
        float dropHeight = Mathf.Max(0.18f, dropTop - dropBottom);

        AddWorldBox(mountRoot.transform, "CeilingPlate", new Vector3(mountCenter.x, ceilingY - 0.025f, mountCenter.z), new Vector3(0.58f, 0.05f, 0.36f), mountMaterial);
        AddWorldBox(mountRoot.transform, "DropArm", new Vector3(mountCenter.x, dropBottom + (dropHeight * 0.5f), mountCenter.z), new Vector3(0.06f, dropHeight, 0.06f), mountMaterial);
        AddWorldBox(mountRoot.transform, "DisplayBracket", new Vector3(mountCenter.x, displayBounds.max.y + 0.05f, mountCenter.z), new Vector3(0.70f, 0.07f, 0.08f), mountMaterial);
    }

    private static void AddWorldBox(Transform parent, string name, Vector3 center, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetPositionAndRotation(center, Quaternion.identity);
        box.transform.localScale = size;
        box.transform.SetParent(parent, true);

        Renderer renderer = box.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    private Material GetMaterial(Color color)
    {
        Material material;
        if (materialsByColor.TryGetValue(color, out material) && material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        material = new Material(shader);
        material.color = color;
        materialsByColor[color] = material;
        return material;
    }

    private void ClearRoot(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            DestroyProp(root.GetChild(i).gameObject);
        }
    }

    private static void DestroyProp(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return;
        }

        Collider[] colliders = gameObject.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Object.DestroyImmediate(gameObject);
        }
        else
        {
            Object.Destroy(gameObject);
        }
#else
        Object.Destroy(gameObject);
#endif
    }

    private static void EnsureColliderFromRenderers(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Collider[] existingColliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < existingColliders.Length; i++)
        {
            if (existingColliders[i] != null)
            {
                existingColliders[i].enabled = false;
            }
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        float padding = 0.005f;
        Transform generatedRoot = root.transform.Find(GeneratedColliderRootName);
        if (generatedRoot == null)
        {
            GameObject generated = new GameObject(GeneratedColliderRootName);
            generated.transform.SetParent(root.transform, false);
            generatedRoot = generated.transform;
        }
        else
        {
            ClearChildObjects(generatedRoot);
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.transform.IsChildOf(generatedRoot))
            {
                continue;
            }

            BoxCollider collider = CreateOrientedRendererCollider(
                renderer,
                generatedRoot,
                renderer.name + "_VisualCollider",
                padding);
            if (collider == null)
            {
                continue;
            }
        }
    }

    private static BoxCollider CreateOrientedRendererCollider(Renderer renderer, Transform parent, string name, float paddingMeters)
    {
        if (renderer == null || parent == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
        {
            return null;
        }

        Bounds localBounds;
        if (!TryGetRendererLocalBounds(renderer, out localBounds) || localBounds.size.sqrMagnitude <= 0.000001f)
        {
            return null;
        }

        GameObject colliderObject = new GameObject(string.IsNullOrWhiteSpace(name) ? renderer.name + "_VisualCollider" : name);
        Vector3 worldScale = AbsScale(renderer.transform.lossyScale);
        colliderObject.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
        colliderObject.transform.localScale = worldScale;
        colliderObject.transform.SetParent(parent, true);

        Vector3 localPadding = WorldPaddingToLocalPadding(Mathf.Max(0f, paddingMeters), worldScale);
        BoxCollider collider = colliderObject.AddComponent<BoxCollider>();
        collider.center = localBounds.center;
        collider.size = new Vector3(
            Mathf.Max(0.0001f, localBounds.size.x + localPadding.x),
            Mathf.Max(0.0001f, localBounds.size.y + localPadding.y),
            Mathf.Max(0.0001f, localBounds.size.z + localPadding.z));
        collider.isTrigger = true;
        collider.enabled = true;
        return collider;
    }

    private static bool TryGetRendererLocalBounds(Renderer renderer, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        if (renderer == null)
        {
            return false;
        }

        SkinnedMeshRenderer skinnedRenderer = renderer as SkinnedMeshRenderer;
        if (skinnedRenderer != null && skinnedRenderer.sharedMesh != null)
        {
            Mesh bakedMesh = new Mesh();
            skinnedRenderer.BakeMesh(bakedMesh);
            bounds = bakedMesh.bounds;
            DestroyTemporaryMesh(bakedMesh);
            return bounds.size.sqrMagnitude > 0.000001f;
        }

        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            bounds = meshFilter.sharedMesh.bounds;
            return true;
        }

        bounds = renderer.localBounds;
        return bounds.size.sqrMagnitude > 0.000001f;
    }

    private static Vector3 AbsScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
    }

    private static Vector3 WorldPaddingToLocalPadding(float paddingMeters, Vector3 worldScale)
    {
        return new Vector3(
            paddingMeters / Mathf.Max(0.0001f, worldScale.x),
            paddingMeters / Mathf.Max(0.0001f, worldScale.y),
            paddingMeters / Mathf.Max(0.0001f, worldScale.z));
    }

    private static void DestroyTemporaryMesh(Mesh mesh)
    {
        if (mesh == null)
        {
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Object.DestroyImmediate(mesh);
        }
        else
        {
            Object.Destroy(mesh);
        }
#else
        Object.Destroy(mesh);
#endif
    }

    private static void ClearChildObjects(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            DestroyProp(root.GetChild(i).gameObject);
        }
    }

    private static Vector3 ResolveScale(SimplePropSpec spec)
    {
        if (spec == null)
        {
            return Vector3.one;
        }

        return new Vector3(
            (spec.minScale.x + spec.maxScale.x) * 0.5f,
            (spec.minScale.y + spec.maxScale.y) * 0.5f,
            (spec.minScale.z + spec.maxScale.z) * 0.5f);
    }

    private static Quaternion ResolveRotation(SimplePropSpec spec)
    {
        if (spec == null)
        {
            return Quaternion.identity;
        }

        return Quaternion.Euler(spec.anchorWorldEulerAngles);
    }

    private static string ResolvePropId(SimplePropSpec spec)
    {
        return spec != null && !string.IsNullOrWhiteSpace(spec.id) ? spec.id : "prop";
    }

    private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;
        if (root == null)
        {
            return false;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private void RecordSummary(SimpleSceneSummary summary)
    {
        LastSummary = summary;
        lastSummaryJson = JsonUtility.ToJson(summary, true);
    }
}
