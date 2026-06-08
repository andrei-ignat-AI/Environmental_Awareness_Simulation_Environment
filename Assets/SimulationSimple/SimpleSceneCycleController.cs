using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum SimpleRobotCaptureMode
{
    ProcedureLinkedAroundTable,
    FreeRoomSnapshot,
}

[DisallowMultipleComponent]
public class SimpleSceneCycleController : MonoBehaviour
{
    [Header("Dependencies")]
    public RobotPoseController poseController;
    public RobotPoseRandomizer poseRandomizer;
    public RobotTableAvoidance tableAvoidance;
    public RobotPoseValidator poseValidator;
    public SimpleRobotProxyRig robotProxyRig;
    public SimpleForbiddenZones forbiddenZones;
    public SimplePropSpawner propSpawner;

    [Header("Runtime Control")]
    public bool generateInitialSceneOnStart = false;
    public bool logSceneSummary = true;
    public int currentSampleIndex = -1;
    public int baseRobotSeed = 3000;
    public int settlingFixedUpdates = 4;
    public int maxRobotPoseAttempts = 12;
    public SimpleRobotCaptureMode captureMode = SimpleRobotCaptureMode.ProcedureLinkedAroundTable;

    [Header("Simple Root Placement")]
    public bool alignRobotRootToTable = true;
    public float aroundTableRootOffsetX = -1.40f;
    public float cranialCaudalRootOffsetX = -1.75f;
    public float freeRoomRootOffsetX = -2.10f;
    public float aroundTableLongTarget = 0.65f;
    public float aroundTableMinimumSwingDegrees = 120f;
    public float aroundTableSwingBackoffDegrees = 90f;
    public float aroundTableSupportDegrees = 105f;

    [Header("Observed State")]
    [SerializeField] private bool generationInProgress;
    [SerializeField] [TextArea(4, 10)] private string lastSceneSummary = "";

    public SimpleSceneSummary LastAcceptedScene { get; private set; }
    public bool IsGenerationInProgress { get { return generationInProgress; } }
    public bool LastGenerationAccepted { get; private set; }
    public string LastGenerationRejectionReason { get; private set; }
    public int LastAcceptedSampleIndex { get; private set; } = -1;
    public int LastAcceptedRobotSeed { get; private set; } = -1;
    public int LastGenerationRobotAttempts { get; private set; }

    private RobotPoseTarget lastAcceptedPose;

    private void Awake()
    {
        AutoResolveDependencies();
        EnsureFallbackPose();
    }

    private void OnValidate()
    {
        AutoResolveDependencies();
    }

    private void Start()
    {
        if (Application.isPlaying && generateInitialSceneOnStart)
        {
            GenerateNextScene();
        }
    }

    private void Update()
    {
        if (!Application.isPlaying || generationInProgress)
        {
            return;
        }

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            GenerateNextScene();
        }
    }

    [ContextMenu("Generate Next Simple Scene")]
    public void GenerateNextScene()
    {
        if (!Application.isPlaying || generationInProgress)
        {
            return;
        }

        AutoResolveDependencies();
        StartCoroutine(GenerateNextSceneRoutine());
    }

    private IEnumerator GenerateNextSceneRoutine()
    {
        generationInProgress = true;
        LastGenerationAccepted = false;
        LastGenerationRejectionReason = "";
        LastGenerationRobotAttempts = 0;

        int candidateSampleIndex = Mathf.Max(0, currentSampleIndex + 1);
        SimpleSceneSummary propSummary = null;
        string propRejectionReason = "prop_spawn_failed";
        if (!propSpawner.TryPrepareCandidateScene(candidateSampleIndex, -1, out propSummary))
        {
            propRejectionReason = propSummary != null ? propSummary.rejectionReason : "prop_spawn_failed";
        }

        if (propSummary == null || propSummary.status != "candidate")
        {
            propSpawner.DiscardCandidateScene();
            yield return RestoreLastAcceptedPose();
            currentSampleIndex = candidateSampleIndex;
            LastGenerationRejectionReason = propRejectionReason;
            lastSceneSummary =
                "sample=" + candidateSampleIndex.ToString("D4") +
                " status=rejected reason=" + propRejectionReason;
            if (logSceneSummary)
            {
                Debug.LogWarning(lastSceneSummary);
            }

            generationInProgress = false;
            yield break;
        }

        RobotPoseProfile resolvedProfile = ResolveRobotProfile(propSummary);
        AlignRobotRootToProfile(resolvedProfile);

        int acceptedRobotSeed = -1;
        RobotPoseTarget candidatePose = null;
        string robotRejectionReason = "pose_generation_failed";

        int attemptCount = Mathf.Max(1, maxRobotPoseAttempts);
        for (int attempt = 0; attempt < attemptCount; attempt++)
        {
            int attemptRobotSeed = baseRobotSeed + candidateSampleIndex + attempt;
            LastGenerationRobotAttempts = attempt + 1;
            RobotPoseTarget attemptedPose = poseRandomizer.SamplePose(resolvedProfile, attemptRobotSeed);
            if (tableAvoidance != null)
            {
                attemptedPose = tableAvoidance.BuildTableAwarePose(attemptedPose, resolvedProfile, attemptRobotSeed, out _);
            }
            attemptedPose = AdjustSimpleProfilePose(attemptedPose, resolvedProfile);

            string applyError;
            if (!poseController.TryApplyPoseTarget(attemptedPose, out applyError))
            {
                robotRejectionReason = applyError;
                continue;
            }

            yield return WaitForSettling();

            string tableSummary;
            if (!forbiddenZones.ValidateRobotAgainstTable(robotProxyRig, out tableSummary))
            {
                robotRejectionReason = tableSummary;
                continue;
            }

            string candidatePropOverlapSummary;
            if (!propSpawner.ValidateRobotAgainstCandidateProps(out candidatePropOverlapSummary))
            {
                robotRejectionReason = candidatePropOverlapSummary;
                continue;
            }

            candidatePose = attemptedPose;
            acceptedRobotSeed = attemptRobotSeed;
            break;
        }

        if (candidatePose == null)
        {
            propSpawner.DiscardCandidateScene();
            yield return RestoreLastAcceptedPose();
            currentSampleIndex = candidateSampleIndex;
            LastGenerationRejectionReason = robotRejectionReason;
            lastSceneSummary = "sample=" + candidateSampleIndex.ToString("D4") + " status=rejected reason=" + robotRejectionReason;
            if (logSceneSummary)
            {
                Debug.LogWarning(lastSceneSummary);
            }

            generationInProgress = false;
            yield break;
        }

        propSummary.robotSeed = acceptedRobotSeed;
        propSpawner.CommitCandidateScene(propSummary);

        string propOverlapSummary;
        if (!propSpawner.ValidateRobotAgainstActiveProps(out propOverlapSummary))
        {
            propSpawner.ClearAllProps();
            yield return RestoreLastAcceptedPose();
            currentSampleIndex = candidateSampleIndex;
            LastGenerationRejectionReason = propOverlapSummary;
            lastSceneSummary =
                "sample=" + candidateSampleIndex.ToString("D4") +
                " status=rejected reason=" + propOverlapSummary;
            if (logSceneSummary)
            {
                Debug.LogWarning(lastSceneSummary);
            }

            generationInProgress = false;
            yield break;
        }

        currentSampleIndex = candidateSampleIndex;
        lastAcceptedPose = ClonePose(candidatePose);
        LastAcceptedScene = propSummary;
        LastAcceptedScene.accepted = true;
        LastAcceptedScene.status = "accepted";
        LastGenerationAccepted = true;
        LastAcceptedSampleIndex = currentSampleIndex;
        LastAcceptedRobotSeed = acceptedRobotSeed;
        LastGenerationRejectionReason = "";

        RobotPoseValidationReport validation = poseValidator != null ? poseValidator.Validate(candidatePose) : null;
        lastSceneSummary =
            "sample=" + currentSampleIndex.ToString("D4") +
            " robotSeed=" + acceptedRobotSeed +
            " profile=" + resolvedProfile +
            " poseValid=" + (validation == null || validation.isValid) +
            " propsPlaced=" + propSummary.placedProps +
            " propsRejected=" + propSummary.rejectedProps;

        if (logSceneSummary)
        {
            Debug.Log(lastSceneSummary);
        }

        generationInProgress = false;
    }

    private RobotPoseProfile ResolveRobotProfile(SimpleSceneSummary sceneSummary)
    {
        if (captureMode == SimpleRobotCaptureMode.FreeRoomSnapshot)
        {
            return RobotPoseProfile.FreeRoomSnapshot;
        }

        int primarySideZ = sceneSummary != null ? sceneSummary.primaryOperatorSideZ : -1;
        // In this simple scene, +Z is physical table right and -Z is physical table left.
        // The existing around-table profile names are kept unchanged; current visual probing showed
        // AroundTableRight lands on the -Z side and AroundTableLeft lands on the +Z side.
        return primarySideZ > 0 ? RobotPoseProfile.AroundTableLeft : RobotPoseProfile.AroundTableRight;
    }

    private IEnumerator RestoreLastAcceptedPose()
    {
        EnsureFallbackPose();
        string error;
        if (lastAcceptedPose != null && poseController.TryApplyPoseTarget(ClonePose(lastAcceptedPose), out error))
        {
            yield return WaitForSettling();
        }
    }

    private IEnumerator WaitForSettling()
    {
        int frameCount = Mathf.Max(1, settlingFixedUpdates);
        for (int i = 0; i < frameCount; i++)
        {
            yield return new WaitForFixedUpdate();
        }
    }

    private void EnsureFallbackPose()
    {
        if (lastAcceptedPose == null && poseController != null)
        {
            lastAcceptedPose = ClonePose(poseController.BuildParkedPose());
        }
    }

    private void AlignRobotRootToProfile(RobotPoseProfile profile)
    {
        if (!alignRobotRootToTable || poseController == null || poseController.robotRoot == null)
        {
            return;
        }

        GameObject table = forbiddenZones != null ? forbiddenZones.tableRoot : null;
        if (table == null)
        {
            return;
        }

        Vector3 position = poseController.robotRoot.transform.position;
        position.x = table.transform.position.x + ResolveRootOffsetX(profile);
        poseController.robotRoot.transform.position = position;
    }

    private float ResolveRootOffsetX(RobotPoseProfile profile)
    {
        switch (profile)
        {
            case RobotPoseProfile.AroundTableLeft:
            case RobotPoseProfile.AroundTableRight:
                return aroundTableRootOffsetX;
            case RobotPoseProfile.CranialCaudal:
                return cranialCaudalRootOffsetX;
            case RobotPoseProfile.FreeRoomSnapshot:
            default:
                return freeRoomRootOffsetX;
        }
    }

    private RobotPoseTarget AdjustSimpleProfilePose(RobotPoseTarget pose, RobotPoseProfile profile)
    {
        if (pose == null)
        {
            return null;
        }

        if (profile != RobotPoseProfile.AroundTableLeft && profile != RobotPoseProfile.AroundTableRight)
        {
            return pose;
        }

        SetJointTarget(pose, "Long", aroundTableLongTarget);

        float preferredSign = profile == RobotPoseProfile.AroundTableLeft ? 1f : -1f;
        float currentSwing = GetJointTarget(pose, "Z1Rot");
        float enforcedMagnitude = Mathf.Max(Mathf.Abs(currentSwing), aroundTableMinimumSwingDegrees);
        float adjustedMagnitude = Mathf.Max(0f, enforcedMagnitude - aroundTableSwingBackoffDegrees);
        float adjustedSwing = preferredSign * adjustedMagnitude;
        SetJointTarget(pose, "Z1Rot", adjustedSwing);
        SetJointTarget(pose, "Z2Rot", -preferredSign * aroundTableSupportDegrees);

        return pose;
    }

    private static float GetJointTarget(RobotPoseTarget pose, string jointName)
    {
        if (pose == null || pose.joints == null)
        {
            return 0f;
        }

        for (int i = 0; i < pose.joints.Count; i++)
        {
            if (pose.joints[i].jointName == jointName)
            {
                return pose.joints[i].target;
            }
        }

        return 0f;
    }

    private static void SetJointTarget(RobotPoseTarget pose, string jointName, float value)
    {
        if (pose == null || pose.joints == null)
        {
            return;
        }

        for (int i = 0; i < pose.joints.Count; i++)
        {
            if (pose.joints[i].jointName == jointName)
            {
                pose.joints[i].target = value;
                return;
            }
        }
    }

    private void AutoResolveDependencies()
    {
        if (poseController == null)
        {
            poseController = FindAnyObjectByType<RobotPoseController>();
        }

        if (poseRandomizer == null)
        {
            poseRandomizer = FindAnyObjectByType<RobotPoseRandomizer>();
        }

        if (tableAvoidance == null)
        {
            tableAvoidance = FindAnyObjectByType<RobotTableAvoidance>();
        }

        if (poseValidator == null)
        {
            poseValidator = FindAnyObjectByType<RobotPoseValidator>();
        }

        if (robotProxyRig == null)
        {
            robotProxyRig = GetComponent<SimpleRobotProxyRig>();
            if (robotProxyRig == null)
            {
                robotProxyRig = FindAnyObjectByType<SimpleRobotProxyRig>();
            }
        }

        if (forbiddenZones == null)
        {
            forbiddenZones = GetComponent<SimpleForbiddenZones>();
            if (forbiddenZones == null)
            {
                forbiddenZones = FindAnyObjectByType<SimpleForbiddenZones>();
            }
        }

        if (propSpawner == null)
        {
            propSpawner = GetComponent<SimplePropSpawner>();
            if (propSpawner == null)
            {
                propSpawner = FindAnyObjectByType<SimplePropSpawner>();
            }
        }
    }

    private static RobotPoseTarget ClonePose(RobotPoseTarget source)
    {
        if (source == null)
        {
            return null;
        }

        RobotPoseTarget clone = new RobotPoseTarget();
        clone.profileName = source.profileName;
        clone.seed = source.seed;

        for (int i = 0; i < source.joints.Count; i++)
        {
            RobotJointCommand joint = source.joints[i];
            clone.joints.Add(new RobotJointCommand
            {
                jointName = joint.jointName,
                semanticRole = joint.semanticRole,
                unit = joint.unit,
                target = joint.target,
            });
        }

        return clone;
    }
}
