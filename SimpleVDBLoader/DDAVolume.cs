using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEditor;
using System.Threading.Tasks;
using System.Threading;

public class DDAVolume : MonoBehaviour
{
    public Object FileIn;
    ComputeShader VolumeShader;
    ComputeBuffer ShadowBuffer;
    ComputeBuffer[] ValidVoxelSitesBuffer;
    ComputeBuffer UnityLightBuffer;
    RenderTexture MainTex;
    RenderTexture VolumeTex;
    Texture3D VolumeTex2;
    Vector4[] NonZeroVoxels;

    [Range(1, 10)]
    public int ShadowDistanceOffset = 1;
    [System.Serializable]
    public struct UnityLight {
        public Vector3 Position;
        public Vector3 Direction;
        public int Type;
        public Vector3 Col;
    }
    UnityLight[] UnityLightData;
    Light[] UnityLights;
    OpenVDBReader[] VDBFileArray;
    Vector3[] Sizes;

    
    private void CreateRenderTexture(ref RenderTexture ThisTex)
    {
        ThisTex = new RenderTexture(Screen.width, Screen.height, 0,
            RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.sRGB);
        ThisTex.enableRandomWrite = true;
        ThisTex.Create();
    }
    void Start()
    {
        UnityLights = Object.FindObjectsOfType<Light>();
        UnityLightData = new UnityLight[UnityLights.Length];
        for(int i = 0; i < UnityLights.Length; i++) {
            Light ThisLight = UnityLights[i];
            Color col = ThisLight.color; 
            UnityLightData[i].Position = ThisLight.transform.position;
            UnityLightData[i].Direction = ThisLight.transform.forward;
            UnityLightData[i].Type = (ThisLight.type == LightType.Point) ? 0 : (ThisLight.type == LightType.Directional) ? 1 : (ThisLight.type == LightType.Spot) ? 2 : 3;
            UnityLightData[i].Col = new Vector3(col[0], col[1], col[2]) * ThisLight.intensity;
        }
        CreateRenderTexture(ref MainTex);
        string CachedString = AssetDatabase.GetAssetPath(FileIn);
        VolumeShader = Resources.Load<ComputeShader>("RenderVolume");
        uint CurVox = 0;
        string[] Materials;
        if(CachedString.Contains(".vdb")) {
            Materials = new string[]{Application.dataPath + CachedString.Replace("Assets", "").Replace("/" + FileIn.name, "\\" + FileIn.name)};            
        } else {
            Materials = System.IO.Directory.GetFiles(Application.dataPath + CachedString.Replace("Assets", ""));
        }
        VDBFileArray = new OpenVDBReader[Materials.Length];
        List<string> Material3 = new List<string>();
        for(int i2 = 0; i2 < Materials.Length; i2++) {
            if(Materials[i2].Contains("meta")) continue;
            if(!Materials[i2].Contains("vdb")) continue;
            Material3.Add(Materials[i2]);
        }
        Materials = Material3.ToArray();
        Sizes = new Vector3[Materials.Length];
        ValidVoxelSitesBuffer = new ComputeBuffer[Materials.Length];
        List<Task> RunningTasks = new List<Task>();
        for(int i2 = 0; i2 < Materials.Length; i2++) {
            var A = i2;
            VDBFileArray[A] = new OpenVDBReader();
            Task t1 = Task.Run(() => { VDBFileArray[A].ParseVDB(Materials[A], A);});
            RunningTasks.Add(t1);
        }

        while(RunningTasks.Count != 0) {
            int TaskCount = RunningTasks.Count;
            for(int i = TaskCount - 1; i >= 0; i--) {
                if (RunningTasks[i].IsFaulted) {
                    Debug.Log(RunningTasks[i].Exception);
                    RunningTasks.RemoveAt(i);
                } else if(RunningTasks[i].Status == TaskStatus.RanToCompletion) {
                    RunningTasks.RemoveAt(i);
                }
            }
        }

        int CurGrid = 0;
        for(int i2 = 0; i2 < Materials.Length; i2++) {
            OpenVDBReader VDBFile = VDBFileArray[i2];
            Vector3 OrigionalSize = new Vector3(VDBFile.Grids[CurGrid].Size.x, VDBFile.Grids[CurGrid].Size.z, VDBFile.Grids[CurGrid].Size.y);
            NonZeroVoxels = new Vector4[VDBFile.Grids[CurGrid].Centers.Count]; 
            VDBFile.Size = OrigionalSize;

            int RepCount = 0;
            OpenVDBReader.Node4 CurNode;
            OpenVDBReader.Node3 CurNode2;
            OpenVDBReader.Voxel Vox;
            Vector3Int ijk = new Vector3Int(0,0,0);
            Vector3 location2 = Vector3.zero;
            uint CurOffset = 0;
            for(int i = 0; i < VDBFile.Grids[CurGrid].Centers.Count; i++) {
                ulong BitIndex1 = (ulong)((((int)VDBFile.Grids[CurGrid].Centers[i].x & 4095) >> 7) | ((((int)VDBFile.Grids[CurGrid].Centers[i].y & 4095) >> 7) << 5) | ((((int)VDBFile.Grids[CurGrid].Centers[i].z & 4095) >> 7) << 10));
                ulong BitIndex2 = (ulong)((((int)VDBFile.Grids[CurGrid].Centers[i].x & 127) >> 3) | ((((int)VDBFile.Grids[CurGrid].Centers[i].y & 127) >> 3) << 4) | ((((int)VDBFile.Grids[CurGrid].Centers[i].z & 127) >> 3) << 8));
                ulong BitIndex3 = (ulong)((((int)VDBFile.Grids[CurGrid].Centers[i].x & 7) >> 0) | ((((int)VDBFile.Grids[CurGrid].Centers[i].y & 7) >> 0) << 3) | ((((int)VDBFile.Grids[CurGrid].Centers[i].z & 7) >> 0) << 6));

                if(VDBFile.Grids[CurGrid].RootNode.Children.TryGetValue(BitIndex1, out CurNode)) {
                    if(CurNode.Children.TryGetValue(BitIndex2, out CurNode2)) {
                        if(CurNode2.Children.TryGetValue(BitIndex3, out Vox)) {
                            location2 = new Vector3(VDBFile.Grids[CurGrid].Centers[i].z, VDBFile.Grids[CurGrid].Centers[i].x, VDBFile.Grids[CurGrid].Centers[i].y);
                            float Val = System.BitConverter.ToSingle(System.BitConverter.GetBytes((uint)Vox.Density)) * 100000000000000000000000000000000000000.0f * 50.0f;
                            if(Val > 0.01f) {
                                NonZeroVoxels[CurOffset] = new Vector4(location2.x, location2.y, location2.z, Val);
                                CurOffset++;
                            }
                        }
                    }
                }
            }
            VDBFileArray[i2] = null;
            ValidVoxelSitesBuffer[i2] = new ComputeBuffer((int)CurOffset, 16);
            ValidVoxelSitesBuffer[i2].SetData(NonZeroVoxels);
            VolumeShader.SetVector("Size", VDBFile.Size);
            Sizes[i2] = VDBFile.Size;
        }
        VolumeTex2 = new Texture3D((int)Sizes[0].x, (int)Sizes[0].y, (int)Sizes[0].z, TextureFormat.RFloat, false);
        Debug.Log("Active Voxels: " + NonZeroVoxels.Length + ", Inactive Voxels: " + (VolumeTex2.width * VolumeTex2.height * VolumeTex2.depth - NonZeroVoxels.Length));
        VolumeTex = new RenderTexture((int)Sizes[0].x, (int)Sizes[0].y, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.sRGB);
        VolumeTex.enableRandomWrite = true;
        VolumeTex.volumeDepth = (int)Sizes[0].z;
        VolumeTex.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        VolumeTex.Create();
        ShadowBuffer = new ComputeBuffer(VolumeTex2.width * VolumeTex2.height * VolumeTex2.depth, 8);
        UnityLightBuffer = new ComputeBuffer(UnityLights.Length, 40);
        VolumeShader.SetBuffer(2, "ShadowBuffer", ShadowBuffer);
        VolumeShader.SetBuffer(0, "ShadowBuffer", ShadowBuffer);
        VolumeShader.SetBuffer(1, "ShadowBuffer", ShadowBuffer);
        VolumeShader.SetBuffer(2, "UnityLights", UnityLightBuffer);
        VolumeShader.SetInt("ScreenWidth", Screen.width);
        VolumeShader.SetInt("ScreenHeight", Screen.height);
    }

    void OnApplicationQuit()
    {
        UnityLightBuffer.Release();
        VolumeTex.Release();
        if (ValidVoxelSitesBuffer != null) for(int i = 0; i < ValidVoxelSitesBuffer.Length; i++) ValidVoxelSitesBuffer[i].Release();
    }
    float CurFrame = 0;
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        VolumeShader.SetInt("CurFrame", (int)Mathf.Floor(CurFrame));
        VolumeShader.SetInt("LightCount", UnityLights.Length);
        int i = (int)Mathf.Floor(CurFrame) % (ValidVoxelSitesBuffer.Length);
        bool HasChanged = false;
        for(int i2 = 0; i2 < UnityLights.Length; i2++) {
            Light ThisLight = UnityLights[i2];
            Color col = ThisLight.color;
            if(ThisLight.transform.hasChanged) {
                HasChanged = true;
                ThisLight.transform.hasChanged = false;
                UnityLightData[i2].Position = ThisLight.transform.position;
                UnityLightData[i2].Direction = ThisLight.transform.forward;
            } 
            int Type = (ThisLight.type == LightType.Point) ? 0 : (ThisLight.type == LightType.Directional) ? 1 : (ThisLight.type == LightType.Spot) ? 2 : 3;
            if(UnityLightData[i2].Type != Type) {
                HasChanged = true;
                UnityLightData[i2].Type = Type;
            }
            if(UnityLightData[i2].Type == 1) VolumeShader.SetVector("SunDir", UnityLightData[i2].Direction);
            Vector3 Col = new Vector3(col[0], col[1], col[2]) * ThisLight.intensity;
            if(!UnityLightData[i2].Col.Equals(Col)) {
                HasChanged = true;
                UnityLightData[i2].Col = Col;
            }
        }
        UnityLightBuffer.SetData(UnityLightData);

        if(Sizes.Length > 1 || CurFrame < 2) {
            VolumeShader.SetVector("Size", Sizes[(int)Mathf.Floor(CurFrame) % (ValidVoxelSitesBuffer.Length)]);
            VolumeShader.SetBuffer(1, "NonZeroVoxels", ValidVoxelSitesBuffer[i]);
            VolumeShader.SetTexture(1, "DDATextureWrite", VolumeTex);
            VolumeShader.SetTexture(3, "DDATextureWrite", VolumeTex);
            VolumeShader.Dispatch(3, Mathf.CeilToInt(Sizes[i].x / 8.0f), Mathf.CeilToInt(Sizes[i].y / 8.0f), Mathf.CeilToInt(Sizes[i].z / 8.0f));
    
            VolumeShader.Dispatch(1, Mathf.CeilToInt(ValidVoxelSitesBuffer[i].count / 1023.0f), 1, 1);
            Graphics.CopyTexture(VolumeTex, VolumeTex2);
        }
            

        VolumeShader.SetTexture(0, "DDATexture", VolumeTex2);
        VolumeShader.SetTexture(2, "DDATexture", VolumeTex2);
        VolumeShader.SetBuffer(2, "NonZeroVoxels", ValidVoxelSitesBuffer[i]);
        
        VolumeShader.SetInt("ShadowDistanceOffset", ShadowDistanceOffset);

        if(CurFrame < 2 || HasChanged || Sizes.Length > 1) {VolumeShader.Dispatch(2, Mathf.CeilToInt(ValidVoxelSitesBuffer[i].count / 1023.0f), 1, 1);}


        VolumeShader.SetMatrix("_CameraInverseProjection", Camera.main.projectionMatrix.inverse);
        VolumeShader.SetMatrix("CameraToWorld", Camera.main.cameraToWorldMatrix);
        VolumeShader.SetTexture(0, "Result", MainTex);
        VolumeShader.Dispatch(0, Mathf.CeilToInt(Screen.width / 8.0f), Mathf.CeilToInt(Screen.height / 8.0f), 1);
        Graphics.Blit(MainTex, destination);
        CurFrame += 0.3f;
    }


}
