using UnityEngine;

/// <summary>
/// Configures robot assembly stability only.
/// No input handling, randomization, prop logic, or capture logic.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-500)]
public class RobotStabilitySetup : MonoBehaviour
{
    [Header("Robot Reference")]
    [Tooltip("Assign the FlexArm root GameObject.")]
    public GameObject robotRoot;

    [Header("Drive Hold Settings")]
    public float driveStiffness = 100000f;
    public float driveDamping = 10000f;
    public float driveForceLimit = 100000f;

    [Header("Validation")]
    public bool warnIfRootDrifts = true;
    public float rootPositionDriftTolerance = 0.02f;
    public float rootRotationDriftToleranceDeg = 2f;

    private ArticulationBody articulationRoot;
    private Vector3 rootAnchorPosition;
    private Quaternion rootAnchorRotation;
    private bool initialized;

    void Start()
    {
        if (robotRoot == null)
        {
            Debug.LogError("RobotStabilitySetup: Assign robotRoot (FlexArm).");
            return;
        }

        ArticulationBody[] allBodies = robotRoot.GetComponentsInChildren<ArticulationBody>(true);
        if (allBodies == null || allBodies.Length == 0)
        {
            Debug.LogError("RobotStabilitySetup: No ArticulationBody components found under robotRoot.");
            return;
        }

        DisableConflictingUrdfControllerScripts();
        ValidateExpectedBodies(allBodies);

        articulationRoot = FindArticulationRoot(allBodies);
        if (articulationRoot == null)
        {
            Debug.LogError("RobotStabilitySetup: Could not find articulation root.");
            return;
        }

        // Pin root and cache anchor for drift monitoring.
        articulationRoot.immovable = true;
        articulationRoot.useGravity = false;
        rootAnchorPosition = articulationRoot.transform.position;
        rootAnchorRotation = articulationRoot.transform.rotation;

        // Gravity off for the full articulated chain.
        for (int i = 0; i < allBodies.Length; i++)
        {
            allBodies[i].useGravity = false;
        }

        // Configure only movable joints to hold pose.
        ConfigureHoldDrive(FindBodyByName(allBodies, "Carriage"));
        ConfigureHoldDrive(FindBodyByName(allBodies, "HorBeam"));
        ConfigureHoldDrive(FindBodyByName(allBodies, "VerBeam"));
        ConfigureHoldDrive(FindBodyByName(allBodies, "Sleeve"));
        ConfigureHoldDrive(FindBodyByName(allBodies, "CArc"));

        initialized = true;

        Debug.Log($"RobotStabilitySetup: Ready. Root={articulationRoot.name}, Pinned={articulationRoot.immovable}, GravityOffBodies={allBodies.Length}");
    }

    void LateUpdate()
    {
        if (!initialized || !warnIfRootDrifts || articulationRoot == null)
        {
            return;
        }

        float posDrift = Vector3.Distance(articulationRoot.transform.position, rootAnchorPosition);
        float rotDrift = Quaternion.Angle(articulationRoot.transform.rotation, rootAnchorRotation);
        if (posDrift > rootPositionDriftTolerance || rotDrift > rootRotationDriftToleranceDeg)
        {
            Debug.LogWarning(
                $"RobotStabilitySetup: Root drift detected. Pos={posDrift:F3}m (tol {rootPositionDriftTolerance:F3}), Rot={rotDrift:F2}deg (tol {rootRotationDriftToleranceDeg:F2}).");
        }
    }

    private void ValidateExpectedBodies(ArticulationBody[] allBodies)
    {
        string[] requiredNames = { "Room", "Rail", "Carriage", "HorBeam", "VerBeam", "Sleeve", "CArc" };
        for (int i = 0; i < requiredNames.Length; i++)
        {
            if (FindBodyByName(allBodies, requiredNames[i]) == null)
            {
                Debug.LogWarning($"RobotStabilitySetup: Expected ArticulationBody not found: {requiredNames[i]}");
            }
        }
    }

    private void ConfigureHoldDrive(ArticulationBody body)
    {
        if (body == null)
        {
            return;
        }

        ArticulationDrive drive = body.xDrive;
        drive.stiffness = driveStiffness;
        drive.damping = driveDamping;
        drive.forceLimit = driveForceLimit;
        drive.target = 0f;
        body.xDrive = drive;
    }

    private void DisableConflictingUrdfControllerScripts()
    {
        MonoBehaviour[] behaviours = robotRoot.GetComponentsInChildren<MonoBehaviour>(true);
        int disabledCount = 0;

        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            string fullName = behaviour.GetType().FullName;
            if (fullName == "Unity.Robotics.UrdfImporter.Control.Controller" ||
                fullName == "FKRobot" ||
                fullName == "JointControl")
            {
                if (behaviour.enabled)
                {
                    behaviour.enabled = false;
                    disabledCount++;
                }
            }
        }

        Debug.Log($"RobotStabilitySetup: Disabled conflicting URDF helper scripts: {disabledCount}");
    }

    private static ArticulationBody FindBodyByName(ArticulationBody[] allBodies, string name)
    {
        for (int i = 0; i < allBodies.Length; i++)
        {
            if (allBodies[i] != null && allBodies[i].gameObject.name == name)
            {
                return allBodies[i];
            }
        }
        return null;
    }

    private static ArticulationBody FindArticulationRoot(ArticulationBody[] allBodies)
    {
        for (int i = 0; i < allBodies.Length; i++)
        {
            ArticulationBody body = allBodies[i];
            if (body == null)
            {
                continue;
            }

            if (FindParentArticulationBody(body.transform) == null)
            {
                return body;
            }
        }

        return null;
    }

    private static ArticulationBody FindParentArticulationBody(Transform t)
    {
        Transform cursor = t.parent;
        while (cursor != null)
        {
            ArticulationBody parentBody = cursor.GetComponent<ArticulationBody>();
            if (parentBody != null)
            {
                return parentBody;
            }
            cursor = cursor.parent;
        }
        return null;
    }
}
