using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SimpleForbiddenZones : MonoBehaviour
{
    private const string GeneratedRootName = "GeneratedSimpleForbiddenZones";

    private readonly List<Collider> forbiddenColliders = new List<Collider>();
    private readonly List<Collider> tableOnlyColliders = new List<Collider>();
    private readonly List<Collider> actualTableColliders = new List<Collider>();

    [Header("Dependencies")]
    public GameObject roomRoot;
    public GameObject tableRoot;

    [Header("Table Volume")]
    public bool includeRailZones = true;
    public float tabletopScaleMultiplier = 1.15f;
    public float railHeightMargin = 0.10f;
    public float railWidthMargin = 0.04f;

    [Header("Room Bounds")]
    public float roomWallClearance = 0.05f;
    public float ceilingClearance = 0.05f;

    private void Awake()
    {
        EnsureBuilt();
    }

    private void OnValidate()
    {
        EnsureBuilt();
    }

    [ContextMenu("Rebuild Simple Forbidden Zones")]
    public void EnsureBuilt()
    {
        AutoResolveDependencies();

        forbiddenColliders.Clear();
        tableOnlyColliders.Clear();

        if (tableRoot == null)
        {
            return;
        }

        SurgeryTableBuilder table = tableRoot.GetComponent<SurgeryTableBuilder>();
        if (table == null)
        {
            return;
        }

        Transform generatedRoot = tableRoot.transform.Find(GeneratedRootName);
        if (generatedRoot == null)
        {
            GameObject root = new GameObject(GeneratedRootName);
            root.transform.SetParent(tableRoot.transform, false);
            generatedRoot = root.transform;
        }

        BuildOrUpdateZone(
            generatedRoot,
            "TableTopForbidden",
            new Vector3(
                table.longitudinalOffset,
                table.topSurfaceHeight - (table.topThickness * 0.5f),
                table.lateralOffset),
            new Vector3(
                table.topLength * tabletopScaleMultiplier,
                table.topThickness * tabletopScaleMultiplier,
                table.topWidth * tabletopScaleMultiplier),
            forbiddenColliders,
            includeInTableList: true);

        if (includeRailZones && table.includeSideRails)
        {
            float railZ = (table.topWidth * 0.5f) - table.railInsetFromEdge - (table.railWidth * 0.5f);
            float railLength = Mathf.Max(0.1f, table.topLength - 0.20f);
            Vector3 railSize = new Vector3(
                railLength + ((table.topLength * (tabletopScaleMultiplier - 1f)) * 0.5f),
                table.railHeight + railHeightMargin,
                table.railWidth + railWidthMargin);

            BuildOrUpdateZone(
                generatedRoot,
                "RailLeftForbidden",
                new Vector3(
                    table.longitudinalOffset,
                    table.topSurfaceHeight + ((table.railHeight + railHeightMargin) * 0.5f) - (table.railHeight * 0.5f),
                    table.lateralOffset - railZ),
                railSize,
                forbiddenColliders,
                includeInTableList: true);

            BuildOrUpdateZone(
                generatedRoot,
                "RailRightForbidden",
                new Vector3(
                    table.longitudinalOffset,
                    table.topSurfaceHeight + ((table.railHeight + railHeightMargin) * 0.5f) - (table.railHeight * 0.5f),
                    table.lateralOffset + railZ),
                railSize,
                forbiddenColliders,
                includeInTableList: true);
        }
    }

    public void CollectTableForbiddenColliders(List<Collider> results)
    {
        CopyColliders(tableOnlyColliders, results);
    }

    public void CollectAllForbiddenColliders(List<Collider> results)
    {
        CopyColliders(forbiddenColliders, results);
    }

    public bool ValidateRobotAgainstTable(SimpleRobotProxyRig proxyRig, out string summary)
    {
        summary = "table_clear";
        if (proxyRig == null)
        {
            summary = "proxy_rig_missing";
            return false;
        }

        List<Collider> robot = new List<Collider>();
        proxyRig.CollectColliders(robot);
        if (robot.Count == 0)
        {
            summary = "robot_proxies_missing";
            return false;
        }

        CollectActualTableColliders(actualTableColliders);
        if (actualTableColliders.Count == 0)
        {
            summary = "table_colliders_missing";
            return false;
        }

        Collider a;
        Collider b;
        float penetrationDistance;
        if (ColliderOverlapUtility.TryFindOverlap(robot, actualTableColliders, out a, out b, out penetrationDistance))
        {
            summary =
                "table_overlap robot=" + a.transform.name +
                " table=" + b.transform.name +
                " penetration=" + penetrationDistance.ToString("F4");
            return false;
        }

        return true;
    }

    public bool TryGetRoomInteriorBounds(out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        AutoResolveDependencies();

        if (roomRoot == null)
        {
            return false;
        }

        CleanRoomBuilder room = roomRoot.GetComponent<CleanRoomBuilder>();
        if (room == null)
        {
            return false;
        }

        bounds = new Bounds(
            roomRoot.transform.position + new Vector3(0f, room.heightY * 0.5f, 0f),
            new Vector3(
                Mathf.Max(0f, room.lengthX - (roomWallClearance * 2f)),
                room.heightY,
                Mathf.Max(0f, room.widthZ - (roomWallClearance * 2f))));
        return true;
    }

    public bool TryGetCeilingY(out float ceilingY)
    {
        ceilingY = 0f;
        Bounds roomBounds;
        if (!TryGetRoomInteriorBounds(out roomBounds))
        {
            return false;
        }

        ceilingY = roomBounds.max.y - ceilingClearance;
        return true;
    }

    public bool TryGetTableSurfaceBounds(out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        AutoResolveDependencies();

        if (tableRoot == null)
        {
            return false;
        }

        SurgeryTableBuilder table = tableRoot.GetComponent<SurgeryTableBuilder>();
        if (table == null)
        {
            return false;
        }

        bounds = new Bounds(
            tableRoot.transform.position + new Vector3(table.longitudinalOffset, table.topSurfaceHeight, table.lateralOffset),
            new Vector3(table.topLength, 0.001f, table.topWidth));
        return true;
    }

    private void BuildOrUpdateZone(
        Transform parent,
        string name,
        Vector3 localPosition,
        Vector3 localSize,
        List<Collider> allResults,
        bool includeInTableList)
    {
        Transform zoneTransform = parent.Find(name);
        GameObject zoneObject;
        if (zoneTransform == null)
        {
            zoneObject = new GameObject(name);
            zoneObject.transform.SetParent(parent, false);
        }
        else
        {
            zoneObject = zoneTransform.gameObject;
        }

        zoneObject.transform.localPosition = localPosition;
        zoneObject.transform.localRotation = Quaternion.identity;
        zoneObject.transform.localScale = Vector3.one;

        BoxCollider collider = zoneObject.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = zoneObject.AddComponent<BoxCollider>();
        }

        collider.center = Vector3.zero;
        collider.size = localSize;
        collider.isTrigger = true;
        collider.enabled = true;

        allResults.Add(collider);
        if (includeInTableList)
        {
            tableOnlyColliders.Add(collider);
        }
    }

    private void AutoResolveDependencies()
    {
        if (roomRoot == null)
        {
            CleanRoomBuilder room = FindAnyObjectByType<CleanRoomBuilder>();
            if (room != null)
            {
                roomRoot = room.gameObject;
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

    private void CollectActualTableColliders(List<Collider> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        if (tableRoot == null)
        {
            return;
        }

        Transform generatedRoot = tableRoot.transform.Find(GeneratedRootName);
        Collider[] colliders = tableRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            if (generatedRoot != null && collider.transform.IsChildOf(generatedRoot))
            {
                continue;
            }

            results.Add(collider);
        }
    }

    private static void CopyColliders(List<Collider> source, List<Collider> destination)
    {
        if (destination == null)
        {
            return;
        }

        destination.Clear();
        for (int i = 0; i < source.Count; i++)
        {
            Collider collider = source[i];
            if (collider != null && collider.enabled)
            {
                destination.Add(collider);
            }
        }
    }

    private static bool TryFindOverlap(IReadOnlyList<Collider> first, IReadOnlyList<Collider> second, out Collider firstCollider, out Collider secondCollider)
    {
        firstCollider = null;
        secondCollider = null;

        Physics.SyncTransforms();

        for (int i = 0; i < first.Count; i++)
        {
            Collider a = first[i];
            if (a == null || !a.enabled)
            {
                continue;
            }

            for (int j = 0; j < second.Count; j++)
            {
                Collider b = second[j];
                if (b == null || !b.enabled)
                {
                    continue;
                }

                if (!a.bounds.Intersects(b.bounds))
                {
                    continue;
                }

                Vector3 direction;
                float distance;
                if (Physics.ComputePenetration(
                    a,
                    a.transform.position,
                    a.transform.rotation,
                    b,
                    b.transform.position,
                    b.transform.rotation,
                    out direction,
                    out distance))
                {
                    firstCollider = a;
                    secondCollider = b;
                    return true;
                }
            }
        }

        return false;
    }
}
