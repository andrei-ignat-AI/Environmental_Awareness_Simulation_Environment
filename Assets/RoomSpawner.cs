using UnityEngine;

public class CleanRoomBuilder : MonoBehaviour
{
    public float lengthX = 10f;
    public float widthZ = 6f;
    public float heightY = 2.7f;
    public float wallThickness = 0.1f;

    [ContextMenu("Build Clean Room")]
    public void BuildCleanRoom()
    {
        // remove old children
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(transform.GetChild(i).gameObject);
            else Destroy(transform.GetChild(i).gameObject);
#else
            Destroy(transform.GetChild(i).gameObject);
#endif
        }

        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        // floor (10 x 6)
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.SetParent(transform, false);
        floor.transform.localPosition = Vector3.zero;
        floor.transform.localScale = new Vector3(lengthX / 10f, 1f, widthZ / 10f);

        // ceiling
        var ceiling = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ceiling.name = "Ceiling";
        ceiling.transform.SetParent(transform, false);
        ceiling.transform.localPosition = new Vector3(0f, heightY, 0f);
        ceiling.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
        ceiling.transform.localScale = new Vector3(lengthX / 10f, 1f, widthZ / 10f);

        // walls: place so inner faces are exactly x=±5 and z=±3
        CreateWall("Wall_left",  new Vector3(-(lengthX * 0.5f + wallThickness * 0.5f), heightY * 0.5f, 0f), new Vector3(wallThickness, heightY, widthZ));
        CreateWall("Wall_right", new Vector3( (lengthX * 0.5f + wallThickness * 0.5f), heightY * 0.5f, 0f), new Vector3(wallThickness, heightY, widthZ));
        CreateWall("Wall_back",  new Vector3(0f, heightY * 0.5f, -(widthZ * 0.5f + wallThickness * 0.5f)), new Vector3(lengthX, heightY, wallThickness));
        CreateWall("Wall_front", new Vector3(0f, heightY * 0.5f,  (widthZ * 0.5f + wallThickness * 0.5f)), new Vector3(lengthX, heightY, wallThickness));
    }

    private void CreateWall(string name, Vector3 localPos, Vector3 localScale)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(transform, false);
        wall.transform.localPosition = localPos;
        wall.transform.localScale = localScale;
    }
}
