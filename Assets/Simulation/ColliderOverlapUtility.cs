using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared narrow-phase overlap helpers for scene safety checks.
/// Callers are expected to supply only the collider sets they actually care about.
/// </summary>
public static class ColliderOverlapUtility
{
    public static void CollectColliders(GameObject root, List<Collider> results)
    {
        CollectColliders(root != null ? root.transform : null, results, null);
    }

    public static void CollectColliders(Transform root, List<Collider> results, Transform excludeRoot = null)
    {
        if (root == null || results == null)
        {
            return;
        }

        results.Clear();
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            if (excludeRoot != null && collider.transform.IsChildOf(excludeRoot))
            {
                continue;
            }

            results.Add(collider);
        }
    }

    public static bool TryGetCombinedBounds(IReadOnlyList<Collider> colliders, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;

        if (colliders == null)
        {
            return false;
        }

        Physics.SyncTransforms();

        for (int i = 0; i < colliders.Count; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    public static bool TryFindOverlap(
        IReadOnlyList<Collider> first,
        IReadOnlyList<Collider> second,
        out Collider firstCollider,
        out Collider secondCollider,
        out float penetrationDistance)
    {
        firstCollider = null;
        secondCollider = null;
        penetrationDistance = 0f;

        if (first == null || second == null)
        {
            return false;
        }

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
                    penetrationDistance = distance;
                    return true;
                }
            }
        }

        return false;
    }
}
