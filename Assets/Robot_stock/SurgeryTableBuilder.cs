using UnityEngine;

/// <summary>
/// Builds a clean, parameterized interventional surgery table in local space.
/// Root transform is the table reference origin on floor level.
/// </summary>
[DisallowMultipleComponent]
public class SurgeryTableBuilder : MonoBehaviour
{
    [Header("Tabletop Specs (Azurion-like)")]
    [Tooltip("Tabletop length in metres.")]
    public float topLength = 3.19f;
    [Tooltip("Tabletop width in metres.")]
    public float topWidth = 0.50f;
    [Tooltip("Tabletop thickness in metres.")]
    public float topThickness = 0.06f;
    [Tooltip("Top surface world height in metres. Typical range: 0.74 to 1.02.")]
    public float topSurfaceHeight = 0.88f;

    [Header("Floating Ranges")]
    [Tooltip("Total longitudinal float range in metres.")]
    public float longitudinalFloatRange = 1.20f;
    [Tooltip("Total lateral float range in metres.")]
    public float lateralFloatRange = 0.36f;
    [Tooltip("Current longitudinal offset from center (metres).")]
    public float longitudinalOffset = 0f;
    [Tooltip("Current lateral offset from center (metres).")]
    public float lateralOffset = 0f;

    [Header("Base Geometry")]
    public float baseHeight = 0.08f;
    public float baseLength = 1.10f;
    public float baseWidth = 0.70f;
    public float pedestalWidth = 0.28f;
    public float pedestalDepth = 0.24f;
    [Tooltip("If enabled, bottom base uses same X/Z footprint as the pedestal (slim base).")]
    public bool baseMatchesPedestalFootprint = true;

    [Header("Support Placement")]
    [Tooltip("Moves pedestal/base along table length (X). Use this to shift support toward one table end.")]
    public float supportLongitudinalOffset = -0.70f;
    [Tooltip("Moves pedestal/base laterally (Z). Keep near zero for typical table geometry.")]
    public float supportLateralOffset = 0f;

    [Header("Optional Rails")]
    public bool includeSideRails = true;
    public float railHeight = 0.02f;
    public float railWidth = 0.02f;
    public float railInsetFromEdge = 0.01f;

    [Header("Visuals")]
    public Color tabletopColor = new Color(0.92f, 0.94f, 0.95f, 1f);
    public Color baseColor = new Color(0.83f, 0.85f, 0.87f, 1f);
    public Color railColor = new Color(0.75f, 0.77f, 0.80f, 1f);

    [Header("Build")]
    public bool rebuildOnStart = false;
    public bool clearExistingChildren = true;

    private Material tabletopMaterial;
    private Material baseMaterial;
    private Material railMaterial;

    private void Start()
    {
        if (rebuildOnStart)
        {
            BuildTable();
        }
    }

    private void OnValidate()
    {
        topLength = Mathf.Max(0.1f, topLength);
        topWidth = Mathf.Max(0.1f, topWidth);
        topThickness = Mathf.Max(0.01f, topThickness);
        topSurfaceHeight = Mathf.Max(topThickness + 0.1f, topSurfaceHeight);

        longitudinalFloatRange = Mathf.Max(0f, longitudinalFloatRange);
        lateralFloatRange = Mathf.Max(0f, lateralFloatRange);

        float halfLong = longitudinalFloatRange * 0.5f;
        float halfLat = lateralFloatRange * 0.5f;
        longitudinalOffset = Mathf.Clamp(longitudinalOffset, -halfLong, halfLong);
        lateralOffset = Mathf.Clamp(lateralOffset, -halfLat, halfLat);

        baseHeight = Mathf.Max(0.02f, baseHeight);
        baseLength = Mathf.Max(0.1f, baseLength);
        baseWidth = Mathf.Max(0.1f, baseWidth);
        pedestalWidth = Mathf.Max(0.05f, pedestalWidth);
        pedestalDepth = Mathf.Max(0.05f, pedestalDepth);

        float effectiveBaseLength = baseMatchesPedestalFootprint ? pedestalWidth : baseLength;
        float effectiveBaseWidth = baseMatchesPedestalFootprint ? pedestalDepth : baseWidth;

        float maxSupportX = Mathf.Max(0f, (topLength * 0.5f) - (effectiveBaseLength * 0.5f));
        float maxSupportZ = Mathf.Max(0f, (topWidth * 0.5f) - (effectiveBaseWidth * 0.5f));
        supportLongitudinalOffset = Mathf.Clamp(supportLongitudinalOffset, -maxSupportX, maxSupportX);
        supportLateralOffset = Mathf.Clamp(supportLateralOffset, -maxSupportZ, maxSupportZ);

        railHeight = Mathf.Max(0.005f, railHeight);
        railWidth = Mathf.Max(0.005f, railWidth);
        railInsetFromEdge = Mathf.Max(0f, railInsetFromEdge);
    }

    [ContextMenu("Build Surgery Table")]
    public void BuildTable()
    {
        if (clearExistingChildren)
        {
            ClearChildren();
        }

        EnsureMaterials();

        float topCenterY = topSurfaceHeight - (topThickness * 0.5f);
        float topOffsetX = longitudinalOffset;
        float topOffsetZ = lateralOffset;

        GameObject top = CreatePart(
            "TableTop",
            PrimitiveType.Cube,
            new Vector3(topOffsetX, topCenterY, topOffsetZ),
            new Vector3(topLength, topThickness, topWidth),
            tabletopMaterial);

        top.isStatic = true;

        if (includeSideRails)
        {
            float railY = topSurfaceHeight - (railHeight * 0.5f);
            float railZ = (topWidth * 0.5f) - railInsetFromEdge - (railWidth * 0.5f);
            float railLength = Mathf.Max(0.1f, topLength - 0.20f);

            CreatePart(
                "Rail_Left",
                PrimitiveType.Cube,
                new Vector3(topOffsetX, railY, topOffsetZ - railZ),
                new Vector3(railLength, railHeight, railWidth),
                railMaterial).isStatic = true;

            CreatePart(
                "Rail_Right",
                PrimitiveType.Cube,
                new Vector3(topOffsetX, railY, topOffsetZ + railZ),
                new Vector3(railLength, railHeight, railWidth),
                railMaterial).isStatic = true;
        }

        float pedestalHeight = Mathf.Max(0.25f, topCenterY - (topThickness * 0.5f) - baseHeight);
        float pedestalCenterY = baseHeight + (pedestalHeight * 0.5f);
        float supportX = supportLongitudinalOffset;
        float supportZ = supportLateralOffset;
        float builtBaseLength = baseMatchesPedestalFootprint ? pedestalWidth : baseLength;
        float builtBaseWidth = baseMatchesPedestalFootprint ? pedestalDepth : baseWidth;

        CreatePart(
            "Pedestal",
            PrimitiveType.Cube,
            new Vector3(supportX, pedestalCenterY, supportZ),
            new Vector3(pedestalWidth, pedestalHeight, pedestalDepth),
            baseMaterial).isStatic = true;

        CreatePart(
            "Base",
            PrimitiveType.Cube,
            new Vector3(supportX, baseHeight * 0.5f, supportZ),
            new Vector3(builtBaseLength, baseHeight, builtBaseWidth),
            baseMaterial).isStatic = true;

        Debug.Log(
            $"SurgeryTableBuilder: Built table. Top={topLength:F2}x{topWidth:F2}m, TopHeight={topSurfaceHeight:F2}m, Float(x/z)={longitudinalOffset:F2}/{lateralOffset:F2}m, Support(x/z)={supportX:F2}/{supportZ:F2}m");
    }

    private GameObject CreatePart(
        string name,
        PrimitiveType primitiveType,
        Vector3 localPosition,
        Vector3 localScale,
        Material material)
    {
        GameObject go = GameObject.CreatePrimitive(primitiveType);
        go.name = name;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = localScale;

        Renderer rendererComponent = go.GetComponent<Renderer>();
        if (rendererComponent != null && material != null)
        {
            rendererComponent.sharedMaterial = material;
        }

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb != null)
        {
            DestroyImmediate(rb);
        }

        return go;
    }

    private void EnsureMaterials()
    {
        if (tabletopMaterial == null)
        {
            tabletopMaterial = BuildMaterial("TabletopMat", tabletopColor);
        }

        if (baseMaterial == null)
        {
            baseMaterial = BuildMaterial("TableBaseMat", baseColor);
        }

        if (railMaterial == null)
        {
            railMaterial = BuildMaterial("TableRailMat", railColor);
        }
    }

    private static Material BuildMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material mat = new Material(shader);
        mat.name = name;
        mat.color = color;
        return mat;
    }

    private void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }
}
