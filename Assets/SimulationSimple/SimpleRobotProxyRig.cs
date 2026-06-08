using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SimpleRobotProxyRig : MonoBehaviour
{
    [System.Serializable]
    public class ProxyDefinition
    {
        public string name;
        public string linkName;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 size = Vector3.one * 0.25f;
        public bool enabled = true;
    }

    private const string GeneratedRootName = "GeneratedSimpleProxies";

    [Header("Dependencies")]
    public RobotPoseController controller;

    [Header("Visual Proxy Generation")]
    public bool useVisualMeshProxies = false;
    public float visualProxyPadding = 0.005f;
    public List<string> visualProxyLinkNames = new List<string>
    {
        "Carriage",
        "HorBeam",
        "VerBeam",
        "Sleeve",
        "CArc",
    };

    [Header("Proxy Definitions")]
    public List<ProxyDefinition> proxies = new List<ProxyDefinition>
    {
        new ProxyDefinition
        {
            name = "SleeveProxy",
            linkName = "Sleeve",
            localPosition = new Vector3(0.05f, 0f, 0f),
            size = new Vector3(0.78f, 0.34f, 0.34f),
        },
        new ProxyDefinition
        {
            name = "CArcUpperProxy",
            linkName = "CArc",
            localPosition = new Vector3(0f, 0.58f, 0f),
            size = new Vector3(0.92f, 0.22f, 0.22f),
        },
        new ProxyDefinition
        {
            name = "CArcLowerProxy",
            linkName = "CArc",
            localPosition = new Vector3(0f, -0.58f, 0f),
            size = new Vector3(0.92f, 0.22f, 0.22f),
        },
        new ProxyDefinition
        {
            name = "VerBeamProxy",
            linkName = "VerBeam",
            localPosition = new Vector3(0.65f, 0f, 0f),
            size = new Vector3(1.05f, 0.28f, 0.28f),
            enabled = false,
        },
    };

    private readonly List<Collider> builtColliders = new List<Collider>();

    private void Awake()
    {
        AutoResolveDependencies();
    }

    private void OnValidate()
    {
        AutoResolveDependencies();
    }

    public void CollectColliders(List<Collider> results)
    {
        if (results == null)
        {
            return;
        }

        EnsureBuilt();
        results.Clear();
        for (int i = 0; i < builtColliders.Count; i++)
        {
            Collider collider = builtColliders[i];
            if (collider != null && collider.enabled)
            {
                results.Add(collider);
            }
        }
    }

    [ContextMenu("Rebuild Simple Robot Proxies")]
    public void EnsureBuilt()
    {
        AutoResolveDependencies();
        builtColliders.Clear();

        if (controller == null || controller.robotRoot == null)
        {
            return;
        }

        ClearGeneratedProxyRoots();
        if (useVisualMeshProxies && BuildVisualMeshProxies() > 0)
        {
            return;
        }

        for (int i = 0; i < proxies.Count; i++)
        {
            ProxyDefinition definition = proxies[i];
            if (definition == null || !definition.enabled)
            {
                continue;
            }

            Transform link = FindNamedTransform(controller.robotRoot.transform, definition.linkName);
            if (link == null)
            {
                continue;
            }

            Transform generatedRoot = link.Find(GeneratedRootName);
            if (generatedRoot == null)
            {
                GameObject generated = new GameObject(GeneratedRootName);
                generated.transform.SetParent(link, false);
                generatedRoot = generated.transform;
            }

            Transform existingProxy = generatedRoot.Find(definition.name);
            GameObject proxyObject;
            if (existingProxy == null)
            {
                proxyObject = new GameObject(definition.name);
                proxyObject.transform.SetParent(generatedRoot, false);
            }
            else
            {
                proxyObject = existingProxy.gameObject;
            }

            proxyObject.transform.localPosition = definition.localPosition;
            proxyObject.transform.localRotation = Quaternion.Euler(definition.localEulerAngles);
            proxyObject.transform.localScale = Vector3.one;

            BoxCollider collider = proxyObject.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = proxyObject.AddComponent<BoxCollider>();
            }

            collider.isTrigger = true;
            collider.center = Vector3.zero;
            collider.size = definition.size;
            collider.enabled = true;
            builtColliders.Add(collider);
        }
    }

    private void AutoResolveDependencies()
    {
        if (controller == null)
        {
            controller = FindAnyObjectByType<RobotPoseController>();
        }
    }

    private int BuildVisualMeshProxies()
    {
        int builtCount = 0;
        if (visualProxyLinkNames == null)
        {
            return builtCount;
        }

        for (int i = 0; i < visualProxyLinkNames.Count; i++)
        {
            string linkName = visualProxyLinkNames[i];
            if (string.IsNullOrWhiteSpace(linkName))
            {
                continue;
            }

            Transform link = FindNamedTransform(controller.robotRoot.transform, linkName);
            if (link == null)
            {
                continue;
            }

            Transform visuals = link.Find("Visuals");
            if (visuals == null)
            {
                continue;
            }

            Transform generatedRoot = EnsureGeneratedRoot(link);
            ClearChildren(generatedRoot);

            Renderer[] renderers = visuals.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Bounds worldBounds = renderer.bounds;
                if (worldBounds.size.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                GameObject proxyObject = new GameObject(linkName + "_" + renderer.name + "_VisualProxy");
                proxyObject.transform.SetPositionAndRotation(worldBounds.center, Quaternion.identity);
                proxyObject.transform.localScale = Vector3.one;
                proxyObject.transform.SetParent(generatedRoot, true);

                BoxCollider collider = proxyObject.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.center = Vector3.zero;
                collider.size = worldBounds.size + (Vector3.one * Mathf.Max(0f, visualProxyPadding));
                collider.enabled = true;
                builtColliders.Add(collider);
                builtCount++;
            }
        }

        return builtCount;
    }

    private void ClearGeneratedProxyRoots()
    {
        if (controller == null || controller.robotRoot == null)
        {
            return;
        }

        Transform[] transforms = controller.robotRoot.GetComponentsInChildren<Transform>(true);
        for (int i = transforms.Length - 1; i >= 0; i--)
        {
            Transform current = transforms[i];
            if (current != null && current.name == GeneratedRootName)
            {
                ClearChildren(current);
            }
        }
    }

    private static Transform EnsureGeneratedRoot(Transform link)
    {
        Transform generatedRoot = link.Find(GeneratedRootName);
        if (generatedRoot == null)
        {
            GameObject generated = new GameObject(GeneratedRootName);
            generated.transform.SetParent(link, false);
            generatedRoot = generated.transform;
        }

        return generatedRoot;
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(root.GetChild(i).gameObject);
            }
            else
            {
                Destroy(root.GetChild(i).gameObject);
            }
#else
            Destroy(root.GetChild(i).gameObject);
#endif
        }
    }

    private static Transform FindNamedTransform(Transform root, string targetName)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == targetName)
            {
                return transforms[i];
            }
        }

        return null;
    }
}
