using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEditor;

public class OpenVDBReader
{
    public Object FileIn;
    public int InstanceNumber;
    public OpenVDBReader() {}
    List<float> Densitys;
    string ParsedString;
    public Vector3 Size;
    public Vector3 MinSize;
    public List<Vector3> Centers;

    public struct Grid {
        public Node5 RootNode;
        public string Name;
        public Vector3 Center;
        public Matrix4x4 VDBTransform;
        public Vector3 Size;
        public List<Vector3> Centers;

    }

    private Vector3 GetPosition(ulong Index, uint SizeIndex)
    {
        Vector3 location = new Vector3();
        location.x = (Index % SizeIndex);
        location.y = (Index / SizeIndex) % SizeIndex;
        location.z = (Index / (SizeIndex * SizeIndex));
        return location;
    }

    void ReadString(ref BinaryReader reader) {
            uint NameLength = System.BitConverter.ToUInt32(reader.ReadBytes(4));
            byte[] Name = reader.ReadBytes((int)NameLength);
            ParsedString = System.Text.Encoding.ASCII.GetString(Name);
    }

    void ReadMetadata(ref BinaryReader reader, ref Grid grid) {
            uint MetaDataCount = System.BitConverter.ToUInt32(reader.ReadBytes(4));
            for(uint i = 0; i < MetaDataCount; i++) {
                ReadString(ref reader);   
                string Selection = ParsedString;
                ReadString(ref reader);
                if(ParsedString.Equals("string")) {
                    ReadString(ref reader);
                } else if(ParsedString.Equals("bool")) {
                    uint ByteCount = System.BitConverter.ToUInt32(reader.ReadBytes(4));
                    reader.ReadBytes(1);
                } else if(ParsedString.Equals("vec3i")) {
                    reader.ReadBytes(4);
                    if(Selection.Equals("file_bbox_max")) {
                        MinSize = new Vector3((float)System.BitConverter.ToUInt32(reader.ReadBytes(4)), (float)System.BitConverter.ToUInt32(reader.ReadBytes(4)), (float)System.BitConverter.ToUInt32(reader.ReadBytes(4)));
                    } else {
                        Size = new Vector3((float)System.BitConverter.ToUInt32(reader.ReadBytes(4)), (float)System.BitConverter.ToUInt32(reader.ReadBytes(4)), (float)System.BitConverter.ToUInt32(reader.ReadBytes(4)));
                        grid.Size = Size;
                    }
                } else {
                    reader.ReadBytes((int)System.BitConverter.ToUInt32(reader.ReadBytes(4)));
                }
            }
    }

    void ParseTransform(ref BinaryReader reader, ref Matrix4x4 VDBTransform) {
        for(int i = 0; i < 4; i++) {
            for(int j = 0; j < 4; j++) {
                VDBTransform[j,i] = (float)System.BitConverter.ToDouble(reader.ReadBytes(8));

            }
        }
    }

    [System.Serializable]
    public struct Node5 {
        public Dictionary<ulong, Node4> Children;
        public ulong[] Mask;
        public ulong[] ValueMask;
        public uint[] Values;
    }
    [System.Serializable]
    public struct Node4 {
        public int Offset;
        public Vector3 Center;
        public Dictionary<ulong, Node3> Children;
        public ulong[] Mask;
        public ulong[] ValueMask;
        public uint[] Values;
    }
    [System.Serializable]
    public struct Node3 {
        public int Offset;
        public Vector3 Center;
        public Dictionary<ulong, Voxel> Children;
        public ulong[] Mask;
    }
    [System.Serializable]
    public struct Voxel {
        public ushort Density;
    }

    int BitCount(ulong A) {
        return (A == 0) ? 32 : (int) (31 - Mathf.Log((long)A & -(long)A, 2));
    }

    public Grid[] Grids;

    void ReadGrid(ref BinaryReader reader, ref Grid grid) {
            ReadString(ref reader);
            grid.Name = ParsedString;

            ReadString(ref reader);
            reader.ReadBytes(28);
            uint Compresed = System.BitConverter.ToUInt32(reader.ReadBytes(4));//compression
            if(Compresed != 0) {
                Debug.Log("File needs to not be compressed");
                return;
            }
            ReadMetadata(ref reader, ref grid);

            ReadString(ref reader);

            ParseTransform(ref reader, ref grid.VDBTransform);

            reader.ReadBytes(32);
            grid.Center = new Vector3((float)System.BitConverter.ToUInt32(reader.ReadBytes(4)), (float)System.BitConverter.ToUInt32(reader.ReadBytes(4)), (float)System.BitConverter.ToUInt32(reader.ReadBytes(4)));
            grid.RootNode = new Node5();
            grid.RootNode.Mask = new ulong[512];
            grid.RootNode.ValueMask = new ulong[512];
            grid.RootNode.Values = new uint[32768];
            grid.RootNode.Children = new Dictionary<ulong, Node4>();
            for(int i = 0; i < 512; i++) {
                grid.RootNode.Mask[i] = System.BitConverter.ToUInt64(reader.ReadBytes(8));
            }
            for(int i = 0; i < 512; i++) {
                grid.RootNode.ValueMask[i] = System.BitConverter.ToUInt64(reader.ReadBytes(8));
            }
            reader.ReadBytes(1);
            for(int i = 0; i < 32768; i++) {
                grid.RootNode.Values[i] = (uint)System.BitConverter.ToUInt16(reader.ReadBytes(2));
            }
            uint IndexA = 0;

            Voxel CurNode3 = new Voxel();
            Node4 CurNode;
            Node3 CurNode2;
            foreach(ulong A in grid.RootNode.Mask) {
                for(ulong A2 = A; A2 != 0; A2 &= A2 - 1) {
                    ulong bit_index = (ulong)((ulong)((ulong)IndexA * (ulong)64) + (ulong)(31 - BitCount(A2)));
                    CurNode = new Node4();
                    CurNode.Children = new Dictionary<ulong, Node3>();
                    CurNode.Mask = new ulong[64];
                    CurNode.ValueMask = new ulong[64];
                    CurNode.Values = new uint[4096];
                    for(int i = 0; i < 64; i++) {
                        CurNode.Mask[i] = System.BitConverter.ToUInt64(reader.ReadBytes(8));
                    }
                    for(int i = 0; i < 64; i++) {
                        CurNode.ValueMask[i] = System.BitConverter.ToUInt64(reader.ReadBytes(8));
                    }
                    reader.ReadBytes(1);
                    for(int i = 0; i < 4096; i++) {
                        CurNode.Values[i] = (uint)System.BitConverter.ToUInt16(reader.ReadBytes(2));
                    }
                    uint Index = 0;
                        foreach(ulong B in CurNode.Mask) {
                            for(ulong B2 = B; B2 != 0; B2 &= B2 - 1) {
                                CurNode2 = new Node3();
                                CurNode2.Mask = new ulong[8];
                                CurNode2.Children = new Dictionary<ulong, Voxel>();
                                ulong bit_index2 = (ulong)((ulong)((ulong)Index * (ulong)64) + (ulong)(31 - BitCount(B2)));
                                for(int i = 0; i < 8; i++) {
                                    CurNode2.Mask[i] = System.BitConverter.ToUInt64(reader.ReadBytes(8));
                                }
                                CurNode.Children.Add(bit_index2, CurNode2);
                            }
                            Index++;
                        }

                    grid.RootNode.Children.Add(bit_index, CurNode);
                }
                IndexA++;
            }

            byte[] Buffer = new byte[1024];
            IndexA = 0;
            grid.Centers = new List<Vector3>();
            foreach(ulong A in grid.RootNode.Mask) {
                for(ulong A2 = A; A2 != 0; A2 &= A2 - 1) {
                    ulong bit_index = (ulong)((ulong)((ulong)IndexA * (ulong)64) + (ulong)(31 - BitCount(A2)));
                    if(grid.RootNode.Children.TryGetValue(bit_index, out CurNode)) {
                        Vector3 RootNodePos = GetPosition(bit_index, 32) * 128.0f;
                        uint Index = 0;
                        foreach(ulong B in CurNode.Mask) {
                            for(ulong B2 = B; B2 != 0; B2 &= B2 - 1) {
                                ulong bit_index2 = (ulong)((ulong)((ulong)Index * (ulong)64) + (ulong)(31 - BitCount(B2)));
                                Vector3 CurNodePos = GetPosition(bit_index2, 16) * 8;
                                if(CurNode.Children.TryGetValue(bit_index2, out CurNode2)) {
                                    if((CurNodePos).z > Size.z) {
                                     reader.ReadBytes(1089);
                                        continue;
                                    }
                                    reader.ReadBytes(65);
                                    Buffer = reader.ReadBytes(1024);
                                    for(ulong i = 0; i < 512; i++) {
                                        CurNode3.Density = System.BitConverter.ToUInt16(Buffer, (int)i * 2);
                                        if(CurNode3.Density != 0) grid.Centers.Add(RootNodePos + CurNodePos + GetPosition((ulong)i, 8));
                                        CurNode2.Children.Add(i, CurNode3);
                                    }
                                }
                            }
                            Index++;
                        }
                    }

                }
                IndexA++;
            }
        }

    public async void ParseVDB(string CachedString, int InstanceNumber)
    {
        this.InstanceNumber = InstanceNumber;
        Centers = new List<Vector3>();
        var reader = new BinaryReader(new MemoryStream(File.ReadAllBytes(CachedString)));
        reader.ReadBytes(57);
        uint MetaDataCount = System.BitConverter.ToUInt32(reader.ReadBytes(4));
        byte[] MetaData = reader.ReadBytes((int)MetaDataCount);
        uint NumGrids = System.BitConverter.ToUInt32(reader.ReadBytes(4));
        Grids = new Grid[NumGrids];
        for(int i = 0; i < NumGrids; i++) {
            ReadGrid(ref reader, ref Grids[i]);
        }
        reader.Close();
    }




}
