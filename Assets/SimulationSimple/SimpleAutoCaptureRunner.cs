using System.Collections;
using UnityEngine;

public enum CameraRigExportMode
{
    ComparisonOfAllConfigurations = 0,
    Cam4Classic = 1,
    Cam3 = 2,
    Cam4Asym = 3,
    Cam5 = 4,
}

[DisallowMultipleComponent]
public class SimpleAutoCaptureRunner : MonoBehaviour
{
    [Header("Dependencies")]
    public SimpleSceneCycleController sceneController;
    public SimpleSampleExporter sampleExporter;

    [Header("Automation")]
    public bool runOnStart = true;
    public int targetAcceptedSamples = 40;
    public int maxAttemptedScenes = 2000;
    public int settlingFixedUpdatesBeforeExport = 18;
    public float exportDelaySeconds = 0.05f;
    public CameraRigExportMode cameraExportMode = CameraRigExportMode.ComparisonOfAllConfigurations;
    public bool logProgress = true;

    [Header("Runtime Debug")]
    [SerializeField] private bool runInProgress;
    [SerializeField] private int attemptedScenes;
    [SerializeField] private int exportedSamples;
    [SerializeField] private string lastRunSummary = "";

    public bool RunInProgress
    {
        get { return runInProgress; }
    }

    private void Awake()
    {
        AutoResolveDependencies();
    }

    private void OnValidate()
    {
        AutoResolveDependencies();
    }

    private void Start()
    {
        if (Application.isPlaying && runOnStart)
        {
            StartAcceptedCaptureBatch();
        }
    }

    [ContextMenu("Start Accepted Capture Batch")]
    public void StartAcceptedCaptureBatch()
    {
        if (!Application.isPlaying || runInProgress)
        {
            return;
        }

        AutoResolveDependencies();
        StartCoroutine(RunAcceptedCaptureBatchRoutine());
    }

    private IEnumerator RunAcceptedCaptureBatchRoutine()
    {
        runInProgress = true;
        attemptedScenes = 0;
        exportedSamples = 0;
        lastRunSummary = "";

        int maxAttempts = Mathf.Max(1, maxAttemptedScenes);
        while (exportedSamples < Mathf.Max(1, targetAcceptedSamples) && attemptedScenes < maxAttempts)
        {
            attemptedScenes++;
            sceneController.GenerateNextScene();
            yield return new WaitUntil(() => !sceneController.IsGenerationInProgress);

            if (!sceneController.LastGenerationAccepted)
            {
                continue;
            }

            int settleFrames = Mathf.Max(0, settlingFixedUpdatesBeforeExport);
            for (int i = 0; i < settleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Physics.SyncTransforms();
            yield return new WaitForEndOfFrame();
            if (exportDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(exportDelaySeconds);
            }

            string exportSummary;
            if (!TryExportSelectedCameraRigs(sceneController.LastAcceptedSampleIndex, out exportSummary))
            {
                if (logProgress)
                {
                    Debug.LogWarning("SimpleAutoCaptureRunner: export_failed reason=" + exportSummary);
                }
                continue;
            }

            exportedSamples++;
            lastRunSummary =
                "acceptedExports=" + exportedSamples +
                " attemptedScenes=" + attemptedScenes +
                " lastSample=" + sceneController.LastAcceptedSampleIndex +
                " " + exportSummary;

            if (logProgress)
            {
                Debug.Log("SimpleAutoCaptureRunner: " + lastRunSummary);
            }
        }

        if (exportedSamples < Mathf.Max(1, targetAcceptedSamples) && logProgress)
        {
            Debug.LogWarning(
                "SimpleAutoCaptureRunner: stopped_after_max_attempts acceptedExports=" + exportedSamples +
                " attemptedScenes=" + attemptedScenes +
                " targetAcceptedSamples=" + Mathf.Max(1, targetAcceptedSamples));
        }

        runInProgress = false;
    }

    private bool TryExportSelectedCameraRigs(int sampleIndex, out string summary)
    {
        FixedSensorRig.CameraRigPreset[] rigs = ResolveCameraRigs();
        if (rigs.Length == 0)
        {
            summary = "no_camera_rigs_selected";
            return false;
        }

        string lastSummary = "";
        for (int i = 0; i < rigs.Length; i++)
        {
            string captureMode = cameraExportMode == CameraRigExportMode.ComparisonOfAllConfigurations
                ? cameraExportMode.ToString()
                : FixedSensorRig.GetRigExportId(rigs[i]);
            if (!sampleExporter.TryExportAcceptedSample(sampleIndex, rigs[i], captureMode, out lastSummary))
            {
                summary = "rig=" + rigs[i] + " reason=" + lastSummary;
                return false;
            }
        }

        summary = "rigsExported=" + rigs.Length + " lastRigSummary=(" + lastSummary + ")";
        return true;
    }

    private FixedSensorRig.CameraRigPreset[] ResolveCameraRigs()
    {
        switch (cameraExportMode)
        {
            case CameraRigExportMode.Cam4Classic:
                return new[] { FixedSensorRig.CameraRigPreset.Cam4Classic };
            case CameraRigExportMode.Cam3:
                return new[] { FixedSensorRig.CameraRigPreset.Cam3 };
            case CameraRigExportMode.Cam4Asym:
                return new[] { FixedSensorRig.CameraRigPreset.Cam4Asym };
            case CameraRigExportMode.Cam5:
                return new[] { FixedSensorRig.CameraRigPreset.Cam5 };
            case CameraRigExportMode.ComparisonOfAllConfigurations:
            default:
                return new[]
                {
                    FixedSensorRig.CameraRigPreset.Cam4Classic,
                    FixedSensorRig.CameraRigPreset.Cam3,
                    FixedSensorRig.CameraRigPreset.Cam4Asym,
                    FixedSensorRig.CameraRigPreset.Cam5,
                };
        }
    }

    private void AutoResolveDependencies()
    {
        if (sceneController == null)
        {
            sceneController = FindAnyObjectByType<SimpleSceneCycleController>();
        }

        if (sampleExporter == null)
        {
            sampleExporter = GetComponent<SimpleSampleExporter>();
            if (sampleExporter == null)
            {
                sampleExporter = FindAnyObjectByType<SimpleSampleExporter>();
            }
        }
    }
}
