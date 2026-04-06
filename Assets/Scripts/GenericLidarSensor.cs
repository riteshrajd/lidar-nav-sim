using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;


public class GenericLidarSensor : MonoBehaviour
{

    //User configurable shader
    public Shader DepthShader;
    // public Shader RangeShader;

    public ComputeShader ComputeShaderSource;
    // public ComputeShader ComputeShaderRangeSource;

    private ComputeShader depthTextureToPolarDataShader;
    // private ComputeShader depthTextureToRangeShader;

    public string LidarModel = "velodynepuck";

    public float HorizontalFOV = 360f;
    public float VerticalFOV = 30f;
    public int VerticalBeams = 16;
    public int HorizontalBeams = 1024;
    public float SensorRange = 100f;
    public float RangeErrorFactor = 0f;
    public float VerticalFOVStart = -15f;
    public Dictionary<int, float> ReflectivityBasedRange = new Dictionary<int, float>();

    private float[] VerticalGradientAngles;
    private float[] VerticalGradientResolutions;

    [HideInInspector] public List<int> ScanIndex;
    [HideInInspector] public int NumberOfSnapRays;
    [HideInInspector] public int TotalNumberOfData;
    [HideInInspector] public int NumberOfPointsPerSnap;
    [HideInInspector] public float SnapHorizontalFieldOfView = 90f;
    [HideInInspector] public int NumberOfPointsInARing;
    [HideInInspector] public float InitialCameraRotation = 0;

    private int NumberOfSnaps = 3;
    private Vector2[] UVs;
    private Camera cam;
    private RenderTexture InternalTargetTexture;
    private int computeShaderId = -1;
    private int computeRangeShaderId = -1;
    private int threadsX = 1024;
    private ComputeBuffer lidarDataBuffer;
    private ComputeBuffer rangeDataBuffer;
    private ComputeBuffer xz;
    private ComputeBuffer rxz;

    private float verticalResolution;
    private float horizontalResolution;

    private Vector3[] Rays;
    private float CameraRotationOffset;
    //Output array
    private Vector4[] lidarData1 = null;
    private Vector4[] lidarData2 = null;
    private float[] rangeData = null;
    private bool data1 = false;
    private Quaternion InitialRotation;
    private int[] _pointPerSnapIndices;

    public bool isInitialized = false;


    // sigma factor * range gives appropriate sigma for gaussian noise 
    // ref https://autonomoustuff.com/wp-content/uploads/2017/08/M8_Datasheet.pdf
    private const float sigmaFactor = (0.03f / 50);

    void Start()
    {

        // get pattern
        GeneratePattern();

        // configure
        Configure();

        isInitialized = true;
    }


    public void Configure()
    {
        // assign shader
        depthTextureToPolarDataShader = Instantiate<ComputeShader>(ComputeShaderSource);

        //setup data array
        lidarData1 = new Vector4[TotalNumberOfData];
        lidarData2 = new Vector4[TotalNumberOfData];
        rangeData = new float[TotalNumberOfData];

        //load compute shader
        computeShaderId = depthTextureToPolarDataShader.FindKernel("DepthTextureToPolarRanges");

        //setup data buffer
        lidarDataBuffer = new ComputeBuffer(/*count*/ TotalNumberOfData, /*stride*/ sizeof(float) * 4);
        rangeDataBuffer = new ComputeBuffer(/*count*/ TotalNumberOfData, /*stride*/ sizeof(float));
        xz = new ComputeBuffer(/*count*/ NumberOfPointsPerSnap, /*stride*/ sizeof(float) * 2);
        rxz = new ComputeBuffer(/*count*/ NumberOfPointsPerSnap, /*stride*/ sizeof(float) * 2);

        xz.SetData(UVs);
        rxz.SetData(UVs);

        // full data
        depthTextureToPolarDataShader.SetBuffer(computeShaderId, "result", lidarDataBuffer);
        depthTextureToPolarDataShader.SetBuffer(computeShaderId, "xz", xz);

        depthTextureToPolarDataShader.SetInt("ringBins", HorizontalBeams);
        depthTextureToPolarDataShader.SetInt("yawBins", Mathf.CeilToInt(NumberOfPointsPerSnap / VerticalBeams));
        depthTextureToPolarDataShader.SetInt("pitchBins", VerticalBeams);
        depthTextureToPolarDataShader.SetInt("numTotalPoints", NumberOfPointsPerSnap);
        
    }

    public bool LidarUpdate()
    {
        try
        {
            for (int snapIndex = 0; snapIndex < NumberOfSnaps; snapIndex++)
            {
                cam.farClipPlane = SensorRange;
                cam.RenderWithShader(DepthShader, "");

                depthTextureToPolarDataShader.SetInt("snapIndex", snapIndex);
                depthTextureToPolarDataShader.SetTexture(computeShaderId, "source", InternalTargetTexture);
                depthTextureToPolarDataShader.Dispatch(computeShaderId, (NumberOfPointsPerSnap + threadsX - 1) / threadsX, 1, 1);

                cam.transform.Rotate(Vector3.down, SnapHorizontalFieldOfView);
            }


            if (data1)
            {
                data1 = false;
                lidarDataBuffer.GetData(lidarData2);
            }
            else
            {
                data1 = true;
                lidarDataBuffer.GetData(lidarData1);
            }

            cam.transform.localRotation = InitialRotation;

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return false;
        }

    }

    void OnDestroy()
    {
        if (lidarDataBuffer != null)
        {
            lidarDataBuffer.Release();
            rangeDataBuffer.Release();
            lidarDataBuffer = null;
            rangeDataBuffer = null;
        }
    }

    public Vector3[] XZLookUp
    {
        get { return Rays; }
    }

    public Vector2[] UVLookUp
    {
        get { return UVs; }
    }

    public int[] PointsPerSnapIndices
    {
        get { return _pointPerSnapIndices; }
    }

    // public float GetRanges(int pointIndex, bool current = true)
    // {
    //     bool firstBuffer = data1;
    //     if (!current)
    //         firstBuffer = !firstBuffer;
    //
    //
    //     float range;
    //     if (firstBuffer)
    //     {
    //         Vector4 data = lidarData1[pointIndex];
    //         float depth = new Vector3(data.x, data.y, data.z).magnitude;
    //         if (depth >= 0.999)
    //             depth = 0;
    //         range = depth * SensorRange;
    //     }
    //     else
    //     {
    //         Vector4 data = lidarData2[pointIndex];
    //         float depth = new Vector3(data.x, data.y, data.z).magnitude;
    //         if (depth >= 0.999)
    //             depth = 0;
    //         range = depth * SensorRange;
    //     }
    //
    //     // add noise if there RangeErrorFactor > 0
    //     if (RangeErrorFactor > 0)
    //     {
    //         float sigma = sigmaFactor * range * RangeErrorFactor;
    //         range = NextGaussian(range, sigma);
    //     }
    //
    //     return range;
    // }
    //
    // public float GetIntensities(int pointIndex, bool current = true)
    // {
    //     bool firstBuffer = data1;
    //     if (!current)
    //         firstBuffer = !firstBuffer;
    //
    //     if (firstBuffer)
    //     {
    //         Vector4 data = lidarData1[pointIndex];
    //         return data.w - (int)data.w;
    //     }
    //     else
    //     {
    //         Vector4 data = lidarData2[pointIndex];
    //         return data.w - (int)data.w;
    //     }
    // }
    //
    // public Vector3 GetNormals(int pointIndex, bool current = true)
    // {
    //     bool firstBuffer = data1;
    //     if (!current)
    //         firstBuffer = !firstBuffer;
    //
    //     if (firstBuffer)
    //     {
    //         Vector4 data = lidarData1[pointIndex];
    //         return new Vector3(data.x, data.y, data.z).normalized;
    //     }
    //     else
    //     {
    //         Vector4 data = lidarData2[pointIndex];
    //         return new Vector3(data.x, data.y, data.z).normalized;
    //     }
    // }
    //
    // public void GetRangeAndIntensity(int pointIndex, out float range, out float intensity, bool current = true)
    // {
    //     bool firstBuffer = data1;
    //     if (!current)
    //         firstBuffer = !firstBuffer;
    //
    //     if (firstBuffer)
    //     {
    //         Vector4 data = lidarData1[pointIndex];
    //         float depth = new Vector3(data.x, data.y, data.z).magnitude;
    //         if (depth >= 0.999)
    //             depth = 0;
    //         range = depth * SensorRange;
    //         intensity = data.w - (int)data.w;
    //     }
    //     else
    //     {
    //         Vector4 data = lidarData2[pointIndex];
    //         float depth = new Vector3(data.x, data.y, data.z).magnitude;
    //         if (depth >= 0.999)
    //             depth = 0;
    //         range = depth * SensorRange;
    //         intensity = data.w - (int)data.w;
    //     }
    //
    //     // add noise if there RangeErrorFactor > 0
    //     if (RangeErrorFactor > 0)
    //     {
    //         float sigma = sigmaFactor * range * RangeErrorFactor;
    //         range = NextGaussian(range, sigma);
    //     }
    // }
    //
    // public void GetRangeAndIntensityAndId(int pointIndex, out float range, out float intensity, out int id, bool current = true)
    // {
    //     bool firstBuffer = data1;
    //     if (!current)
    //         firstBuffer = !firstBuffer;
    //
    //     if (firstBuffer)
    //     {
    //         Vector4 data = lidarData1[pointIndex];
    //         float depth = new Vector3(data.x, data.y, data.z).magnitude;
    //         if (depth >= 0.999)
    //             depth = 0;
    //         range = depth * SensorRange;
    //         intensity = data.w - (int)data.w;
    //         id = (int)data.w;
    //     }
    //     else
    //     {
    //         Vector4 data = lidarData2[pointIndex];
    //         float depth = new Vector3(data.x, data.y, data.z).magnitude;
    //         if (depth >= 0.999)
    //             depth = 0;
    //         range = depth * SensorRange;
    //         intensity = data.w - (int)data.w;
    //         id = (int)data.w;
    //     }
    // }

    public void GetRangeAndIntensityAndIdAndNormal(int pointIndex,
        out float range,
        out float intensity,
        out int id,
        out Vector3 normal,
        out int reflectivity,
        bool current = true)
    {
        bool firstBuffer = data1;
        if (!current)
            firstBuffer = !firstBuffer;

        Vector4 data;

        if (firstBuffer)
            data = lidarData1[pointIndex];
        else
            data = lidarData2[pointIndex];

        float depth = new Vector3(data.x, data.y, data.z).magnitude;
        if (depth >= 0.999)
            depth = 0;
        range = depth * SensorRange;
        intensity = data.w - (int)data.w;
        reflectivity = Mathf.RoundToInt(intensity * 100);
        id = (int)data.w;
        normal = new Vector3(data.x, data.y, data.z).normalized;

        // add noise if there RangeErrorFactor > 0
        if (RangeErrorFactor > 0)
        {
            float sigma = sigmaFactor * range * RangeErrorFactor;
            range = NextGaussian(range, sigma);
        }
    }

    
    public bool GetRangesAndIntensitiesAndIdAndNormals(out float[] ranges,
        out float[] intensities,
        out int[] ids,
        out Vector3[] normals,
        out int[] reflectivities,
        bool current = true)
    {
        ranges = new float[TotalNumberOfData];
        intensities = new float[TotalNumberOfData];
        ids = new int[TotalNumberOfData];
        normals = new Vector3[TotalNumberOfData];
        reflectivities = new int[TotalNumberOfData];

        bool result = true;
        if (result)
        {
            for (int pointIndex = 0; pointIndex < TotalNumberOfData; pointIndex++)
            {
                int idx = ScanIndex[pointIndex];
                GetRangeAndIntensityAndIdAndNormal(idx,
                    out ranges[pointIndex],
                    out intensities[pointIndex],
                    out ids[pointIndex],
                    out normals[pointIndex],
                    out reflectivities[pointIndex],
                    current);
            }
        }
        return result;
    }

    public void GeneratePattern()
    {
        // set num beams, vertical res, and vertical start for each lidar model

        float tol = 0.5f;
        #region Misc
        if (LidarModel == "hakuyo")
        {
            HorizontalFOV = 360f;
            VerticalBeams = 1;
            verticalResolution = 0;
            VerticalFOVStart = 0;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 5f;
        }
        else if (LidarModel == "quanergym8")
        {
            HorizontalFOV = 360f;
            VerticalBeams = 8;
            verticalResolution = 20f / 7;
            VerticalFOVStart = -17;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 150f;
        }
        else if (LidarModel == "velabit")
        {
            HorizontalFOV = 60f;
            HorizontalBeams = 150;
            VerticalBeams = 10;
            verticalResolution = 10f / (VerticalBeams - 1);
            VerticalFOVStart = -5;
            horizontalResolution = HorizontalFOV / (HorizontalBeams - 1);
            SensorRange = 100f;
        }
        else if (LidarModel == "velodynepuck")
        {
            HorizontalFOV = 360f;
            VerticalBeams = 16;
            verticalResolution = 30f / 15;
            VerticalFOVStart = -15;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 100f;
        }
        else if (LidarModel == "velodynepuckhires")
        {
            HorizontalFOV = 360f;
            VerticalBeams = 16;
            verticalResolution = 20f / 15;
            VerticalFOVStart = -10;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 100f;
        }
        else if (LidarModel == "velodynehdl32e")
        {
            HorizontalFOV = 360f;
            VerticalBeams = 32;
            verticalResolution = 41.33f / 31;
            VerticalFOVStart = -30.67f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 100f;
        }
        else if (LidarModel == "velodyneultrapuck")
        {
            HorizontalFOV = 360f;
            VerticalBeams = 32;
            verticalResolution = 40f / 31;
            VerticalFOVStart = -25;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 200f;
        }
        else if (LidarModel == "velodynehdl64e")
        {
            HorizontalFOV = 360f;
            VerticalBeams = 64;
            verticalResolution = 26.9f / 63;
            VerticalFOVStart = -24.9f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
        }
        else if (LidarModel == "velodynealphapuck")
        {
            HorizontalFOV = 360f;
            VerticalBeams = 128;
            verticalResolution = 40f / 127;
            VerticalFOVStart = -25;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 300f;
        }
        else if (LidarModel == "Leddar-Pixell")
        {
            HorizontalFOV = 180f;
            HorizontalBeams = 96;
            VerticalBeams = 8;
            VerticalFOV = 16f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 40f;
        }
        #endregion

        #region OS0
        else if (LidarModel == "OS0-32-512")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 32;
            VerticalFOV = 90;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-32-1024")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 32;
            VerticalFOV = 90;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-32-2048")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 32;
            VerticalFOV = 90;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-64-512")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 64;
            VerticalFOV = 90;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-64-1024")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 64;
            VerticalFOV = 90;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-64-2048")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 64;
            VerticalFOV = 90;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-128-512")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 128;
            VerticalFOV = 90;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-128-1024")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 128;
            VerticalFOV = 90;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-128-2048")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 128;
            VerticalFOV = 90;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-32-512-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 32;
            VerticalGradientAngles = new float[] { 27f, 9.5f, 5f, -6.5f, -12.5f, -33.5f };
            VerticalGradientResolutions = new float[] { 6.0f, 2.2f, 0.75f, 1.5f, 3.0f };
            VerticalFOVStart = -33.5f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-32-1024-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 32;
            VerticalGradientAngles = new float[] { 27f, 9.5f, 5f, -6.5f, -12.5f, -33.5f };
            VerticalGradientResolutions = new float[] { 6.0f, 2.2f, 0.75f, 1.5f, 3.0f };
            VerticalFOVStart = -33.5f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-32-2048-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 32;
            VerticalGradientAngles = new float[] { 27f, 9.5f, 5f, -6.5f, -12.5f, -33.5f };
            VerticalGradientResolutions = new float[] { 6.0f, 2.2f, 0.75f, 1.5f, 3.0f };
            VerticalFOVStart = -33.5f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-64-512-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 64;
            VerticalGradientAngles = new float[] { 47.5f, 29.5f, 20.5f, 8.5f, -17.5f, -29.5f, -47.5f };
            VerticalGradientResolutions = new float[] { 6.0f, 3.0f, 1.5f, 0.75f, 1.5f, 3.0f };
            VerticalFOVStart = -33.5f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-64-1024-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 64;
            VerticalGradientAngles = new float[] { 47.5f, 29.5f, 20.5f, 8.5f, -17.5f, -29.5f, -47.5f };
            VerticalGradientResolutions = new float[] { 6.0f, 3.0f, 1.5f, 0.75f, 1.5f, 3.0f };
            VerticalFOVStart = -33.5f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        else if (LidarModel == "OS0-64-2048-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 64;
            VerticalGradientAngles = new float[] { 47.5f, 29.5f, 20.5f, 8.5f, -17.5f, -29.5f, -47.5f };
            VerticalGradientResolutions = new float[] { 6.0f, 3.0f, 1.5f, 0.75f, 1.5f, 3.0f };
            VerticalFOVStart = -33.5f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 55f;
            ReflectivityBasedRange.Add(4, 28.0f);
            ReflectivityBasedRange.Add(1, 15.0f);
        }
        #endregion

        #region OS1
        else if (LidarModel == "OS1-32-512")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 32;
            VerticalFOV = 45f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
        }
        else if (LidarModel == "OS1-32-1024")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 32;
            VerticalFOV = 45f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
        }
        else if (LidarModel == "OS1-32-2048")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 32;
            VerticalFOV = 45f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
        }
        else if (LidarModel == "OS1-64-512")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 64;
            VerticalFOV = 45f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
        }
        else if (LidarModel == "OS1-64-1024")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 64;
            VerticalFOV = 45f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
        }
        else if (LidarModel == "OS1-64-2048")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 64;
            VerticalFOV = 45f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
        }
        else if (LidarModel == "OS1-128-512")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 128;
            VerticalFOV = 45f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
        }
        else if (LidarModel == "OS1-128-1024")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 128;
            VerticalFOV = 45f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
        }
        else if (LidarModel == "OS1-128-2048")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 128;
            VerticalFOV = 45f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
        }
        else if (LidarModel == "OS1-32-512-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 32;
            VerticalGradientAngles = new float[] { 13f, 4.5f, 2.5f, -3f, -6f, -16f };
            VerticalGradientResolutions = new float[] { 2.8f, 1.1f, 0.35f, 0.7f, 1.4f };
            VerticalFOVStart = -16f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
            tol = 0.75f;
        }
        else if (LidarModel == "OS1-32-1024-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 32;
            VerticalGradientAngles = new float[] { 13f, 4.5f, 2.5f, -3f, -6f, -16f };
            VerticalGradientResolutions = new float[] { 2.8f, 1.1f, 0.35f, 0.7f, 1.4f };
            VerticalFOVStart = -16f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
            tol = 0.75f;
        }
        else if (LidarModel == "OS1-32-2048-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 32;
            VerticalGradientAngles = new float[] { 13f, 4.5f, 2.5f, -3f, -6f, -16f };
            VerticalGradientResolutions = new float[] { 2.8f, 1.1f, 0.35f, 0.7f, 1.4f };
            VerticalFOVStart = -16f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
            tol = 0.75f;
        }
        else if (LidarModel == "OS1-64-512-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 64;
            VerticalGradientAngles = new float[] { 22.5f, 14f, 9.5f, 4f, -8.5f, -14f, -22.5f };
            VerticalGradientResolutions = new float[] { 2.8f, 1.4f, 0.7f, 0.35f, 0.7f, 1.4f };
            VerticalFOVStart = -22.5f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
            tol = 0.75f;
        }
        else if (LidarModel == "OS1-64-1024-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 64;
            VerticalGradientAngles = new float[] { 22.5f, 14f, 9.5f, 4f, -8.5f, -14f, -22.5f };
            VerticalGradientResolutions = new float[] { 2.8f, 1.4f, 0.7f, 0.35f, 0.7f, 1.4f };
            VerticalFOVStart = -22.5f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
            tol = 0.75f;
        }
        else if (LidarModel == "OS1-64-2048-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 64;
            VerticalGradientAngles = new float[] { 22.5f, 14f, 9.5f, 4f, -8.5f, -14f, -22.5f };
            VerticalGradientResolutions = new float[] { 2.8f, 1.4f, 0.7f, 0.35f, 0.7f, 1.4f };
            VerticalFOVStart = -22.5f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 120f;
            tol = 0.75f;
        }
        #endregion

        #region OS2
        else if (LidarModel == "OS2-32-512")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 32;
            VerticalFOV = 22.5f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        else if (LidarModel == "OS2-32-1024")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 32;
            VerticalFOV = 22.5f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        else if (LidarModel == "OS2-32-2048")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 32;
            VerticalFOV = 22.5f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        else if (LidarModel == "OS2-64-512")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 64;
            VerticalFOV = 22.5f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        else if (LidarModel == "OS2-64-1024")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 64;
            VerticalFOV = 22.5f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        else if (LidarModel == "OS2-64-2048")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 64;
            VerticalFOV = 22.5f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        else if (LidarModel == "OS2-128-512")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 128;
            VerticalFOV = 22.5f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        else if (LidarModel == "OS2-128-1024")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 128;
            VerticalFOV = 22.5f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        else if (LidarModel == "OS2-128-2048")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 128;
            VerticalFOV = 22.5f;
            verticalResolution = VerticalFOV / (VerticalBeams - 1);
            VerticalFOVStart = -(VerticalFOV / 2.0f);
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        else if (LidarModel == "OS2-32-512-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 32;
            VerticalGradientAngles = new float[] { 6.5f, 2f, 1f, -1.5f, -3f, -8f };
            VerticalGradientResolutions = new float[] { 1.4f, 0.5f, 0.18f, 0.5f, 0.7f };
            VerticalFOVStart = -8f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        else if (LidarModel == "OS2-32-1024-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 32;
            VerticalGradientAngles = new float[] { 6.5f, 2f, 1f, -1.5f, -3f, -8f };
            VerticalGradientResolutions = new float[] { 1.4f, 0.5f, 0.18f, 0.5f, 0.7f };
            VerticalFOVStart = -8f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        else if (LidarModel == "OS2-32-2048-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 32;
            VerticalGradientAngles = new float[] { 6.5f, 2f, 1f, -1.5f, -3f, -8f };
            VerticalGradientResolutions = new float[] { 1.4f, 0.5f, 0.18f, 0.5f, 0.7f };
            VerticalFOVStart = -8f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
            tol = 0.00001f;
        }
        else if (LidarModel == "OS2-64-512-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 512;
            VerticalBeams = 64;
            VerticalGradientAngles = new float[] { 11.25f, 7f, 5f, 2f, -4f, -7f, -11.25f };
            VerticalGradientResolutions = new float[] { 1.4f, 0.7f, 0.35f, 0.18f, 0.35f, 0.7f };
            VerticalFOVStart = -11.25f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        else if (LidarModel == "OS2-64-1024-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 1024;
            VerticalBeams = 64;
            VerticalGradientAngles = new float[] { 11.25f, 7f, 5f, 2f, -4f, -7f, -11.25f };
            VerticalGradientResolutions = new float[] { 1.4f, 0.7f, 0.35f, 0.18f, 0.35f, 0.7f };
            VerticalFOVStart = -11.25f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        else if (LidarModel == "OS2-64-2048-Gradient")
        {
            HorizontalFOV = 360f;
            HorizontalBeams = 2048;
            VerticalBeams = 64;
            VerticalGradientAngles = new float[] { 11.25f, 7f, 5f, 2f, -4f, -7f, -11.25f };
            VerticalGradientResolutions = new float[] { 1.4f, 0.7f, 0.35f, 0.18f, 0.35f, 0.7f };
            VerticalFOVStart = -11.25f;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
            SensorRange = 240f;
        }
        #endregion

        else
        {
            verticalResolution = VerticalFOV / VerticalBeams;
            horizontalResolution = HorizontalFOV / HorizontalBeams;
        }

        ReflectivityBasedRange.Add(90, SensorRange);

        List<float> vAngles = new List<float>();
        if (CheckGradientVerticalAngle())
            vAngles = CalculateVerticalGradientAngles(tol);
        if (vAngles.Count == 0)
            vAngles = Enumerable.Range(0, VerticalBeams).Reverse().Select(i => (VerticalFOVStart + i * verticalResolution) * Utils.DEG2RAD).ToList();

        CalculateSnapHorizontalFieldOfView();

        //NumberOfSnaps = Mathf.CeilToInt(HorizontalFOV / SnapHorizontalFieldOfView);
        SnapHorizontalFieldOfView = HorizontalFOV / NumberOfSnaps;
        int horizontalBins = Mathf.CeilToInt(SnapHorizontalFieldOfView / horizontalResolution);
        float hStart = SnapHorizontalFieldOfView / 2f - (horizontalResolution * 0.5f);

        // collect xz coordinate where y = 1
        NumberOfPointsPerSnap = vAngles.Count * horizontalBins;
        Rays = new Vector3[NumberOfPointsPerSnap];
        _pointPerSnapIndices = new int[NumberOfPointsPerSnap];


        Vector2[] hv = new Vector2[NumberOfPointsPerSnap];
        int rayCount = 0;

        //using (StreamWriter writer = new StreamWriter(Path.Combine(@"C:\Sandbox\Data\NodeNetwork\xz_1.csv")))
        //{
            for (int row = 0; row < vAngles.Count; row++)
            {
                for (int col = 0; col < horizontalBins; col++)
                {
                    // get horizontal and vertical angles
                    //float hAngle = (hStart + ((float)col / horizontalBins) * SnapHorizontalFieldOfView) * Utils.DEG2RAD;
                    float hAngle = (hStart - col * horizontalResolution) * Utils.DEG2RAD;
                    float vAngle = vAngles[row];

                    // compute coordinate
                    float hi = 1 / Mathf.Cos(hAngle);
                    float zi = hi * Mathf.Tan(vAngle);
                    float xi = Mathf.Tan(hAngle);

                    // store
                    Rays[rayCount] = new Vector3(xi, zi, row);
                    //writer.WriteLine($"{rayCount},{xi},{zi}");

                    // debug
                    hv[rayCount] = new Vector2(hAngle * Utils.RAD2DEG, vAngle * Utils.RAD2DEG);
                    _pointPerSnapIndices[rayCount] = rayCount;
                    rayCount++;
                }
            }
        //}


        //  calculate uv min and max
        float xmin = Rays.Select(p => p.x).Min();
        float xmax = Rays.Select(p => p.x).Max();
        float zmin = Rays.Select(p => p.y).Min();
        float zmax = Rays.Select(p => p.y).Max();

        UVs = new Vector2[NumberOfPointsPerSnap];
        for (int i = 0; i < Rays.Length; i++)
        {
            // get xz
            var point = Rays[i];
            var xi = point.x;
            var zi = point.y;

            // compute uv
            float u = (xi - xmin) / (xmax - xmin);
            float v = 0.5f;
            if (VerticalBeams > 1)
                v = (zi - zmin) / (zmax - zmin);

            // store
            UVs[i] = new Vector2(u, v);
        }

        //set camera
        cam = GetComponent<Camera>();
        Vector3 InitialAngles = cam.transform.localEulerAngles;
        CameraRotationOffset = -(float)NumberOfSnaps * 0.5f * SnapHorizontalFieldOfView;

        InitialCameraRotation = ((float)NumberOfSnaps - 1.0f) * 0.5f * SnapHorizontalFieldOfView;

        InitialRotation = Quaternion.Euler(InitialAngles.x, InitialCameraRotation, InitialAngles.z);

        cam.transform.localRotation = InitialRotation;

        // set frustum
        float tolerance4SingleBeam = VerticalBeams > 1 ? 0 : 0.01f;

        float top = zmax + (float)Utils.EPSILON + tolerance4SingleBeam;
        float bottom = zmin - (float)Utils.EPSILON - tolerance4SingleBeam;
        float right = xmax + (float)Utils.EPSILON;
        float left = xmin - (float)Utils.EPSILON;
        float near = .01f;
        float far = SensorRange;

        float ratio = near;
        cam.projectionMatrix = PerspectiveOffCenter(left * ratio, right * ratio, bottom * ratio, top * ratio, near, far);

        // cliping plane

        //cam.farClipPlane = SensorRange;

        // target texture
        InternalTargetTexture = cam.targetTexture;

        // initialize beam points

        TotalNumberOfData = NumberOfPointsPerSnap * NumberOfSnaps;
        NumberOfSnapRays = Mathf.CeilToInt(SnapHorizontalFieldOfView / horizontalResolution);
        NumberOfPointsInARing = Mathf.CeilToInt(HorizontalFOV / horizontalResolution);
        //TotalNumberOfScanPoints = NumberOfPointsInRing * NumberOfBeams;

        // remove overlapping points
        //var duplicate = Enumerable.Range(1, TotalNumberOfData - TotalNumberOfScanPoints).Select(i => i * NumberOfSnapRays).ToList();
        ScanIndex = Enumerable.Range(0, TotalNumberOfData).ToList();
        //foreach (int d in duplicate)
        //    ScanIndex.RemoveAt(d);

    }

    private void CalculateSnapHorizontalFieldOfView()
    {
        if (HorizontalFOV > 120f)
        {
            int lowest = HorizontalFOV > 240f ? 3 : 2;
            for (int i = lowest; i < 7; i++)
            {
                if (HorizontalBeams % i == 0)
                {
                    NumberOfSnaps = i;
                    break;
                }
            }

            //SnapHorizontalFieldOfView = HorizontalFOV / lowest;
        }
        else
        {
            NumberOfSnaps = 1;
        }
    }

    private List<float> CalculateVerticalGradientAngles(float tol = 0.5f)
    {
        List<float> verticalAngles = new List<float>();
        for (int i = 0; i < VerticalGradientResolutions.Length; i++)
        {
            float currentAngle = VerticalGradientAngles[i];
            if (verticalAngles.Count != 0 && Mathf.Abs(verticalAngles[verticalAngles.Count - 1] - currentAngle) < (VerticalGradientResolutions[i - 1] * tol))
                verticalAngles.RemoveAt(verticalAngles.Count - 1);
            while (currentAngle >= VerticalGradientAngles[i + 1])
            {
                verticalAngles.Add(currentAngle);

                currentAngle -= VerticalGradientResolutions[i];
            }
        }
        var tmp = verticalAngles;
        return verticalAngles.Select(a => a * Utils.DEG2RAD).ToList();
    }

    private bool CheckGradientVerticalAngle()
    {
        bool isVerticalGradientAnglesNotNull = VerticalGradientAngles != null;
        bool isVerticalGradientResolutionsNotNull = VerticalGradientResolutions != null;
        if (isVerticalGradientAnglesNotNull && isVerticalGradientResolutionsNotNull && VerticalGradientAngles.Length == VerticalGradientResolutions.Length + 1)
            return true;
        else
            return false;
    }

    internal float NextGaussian(float mu = 0, float sigma = 1)
    {
        // use Box-Muller transform to generate Gaussian distribution N(mu,signma) from uniform distribution U[0,1]
        var u1 = UnityEngine.Random.Range(0f, 1f);
        var u2 = UnityEngine.Random.Range(0f, 1f);

        var randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);

        var randNormal = mu + sigma * randStdNormal;

        return randNormal;
    }

    internal Matrix4x4 PerspectiveOffCenter(float left, float right, float bottom, float top, float near, float far)
    {
        float x = 2.0F * near / (right - left);
        float y = 2.0F * near / (top - bottom);
        float a = (right + left) / (right - left);
        float b = (top + bottom) / (top - bottom);
        float c = -(far + near) / (far - near);
        float d = -(2.0F * far * near) / (far - near);
        float e = -1.0F;
        Matrix4x4 m = new Matrix4x4();
        m[0, 0] = x;
        m[0, 1] = 0;
        m[0, 2] = a;
        m[0, 3] = 0;
        m[1, 0] = 0;
        m[1, 1] = y;
        m[1, 2] = b;
        m[1, 3] = 0;
        m[2, 0] = 0;
        m[2, 1] = 0;
        m[2, 2] = c;
        m[2, 3] = d;
        m[3, 0] = 0;
        m[3, 1] = 0;
        m[3, 2] = e;
        m[3, 3] = 0;

        //m[0, 0] = 0.5773503f;
        //m[0, 1] = 0;
        //m[0, 2] = 0;
        //m[0, 3] = 0;
        //m[1, 0] = 0;
        //m[1, 1] = 1.743707f;
        //m[1, 2] = 0;
        //m[1, 3] = 0;
        //m[2, 0] = 0;
        //m[2, 1] = 0;
        //m[2, 2] = -1.01005f;
        //m[2, 3] = -1.005025f;
        //m[3, 0] = 0;
        //m[3, 1] = 0;
        //m[3, 2] = -1;
        //m[3, 3] = 0;
        return m;
    }

}

