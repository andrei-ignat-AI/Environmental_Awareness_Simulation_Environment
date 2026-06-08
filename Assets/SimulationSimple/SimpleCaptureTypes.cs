using System;
using UnityEngine;

[Serializable]
public class SimpleVoxelMetadataExport
{
    public string fileName;
    public string contents;
    public int sizeX;
    public int sizeY;
    public int sizeZ;
    public Vector3 origin;
    public Vector3 voxelSize;
    public int occupiedVoxels;
    public bool propsOnly;
    public string storageOrder = "x + sizeX * (z + sizeZ * y)";
}
