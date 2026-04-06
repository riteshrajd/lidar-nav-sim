using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Time = UnityEngine.Time;


public class GenericLidarRenderer : MonoBehaviour
{
    public EventHandler PublishHandler;
    public Material meshMaterial;
    public bool on = true;
    private Mesh pointMesh;
    public Color[] idColors;
    public bool showSegmentationColors = false;
    public float Period;
    public bool publish = false;
    public bool save = false;
    public string pointCloudTopicName = "pointcloud_msg";
    public string frameTopicName = "tf";

    private Vector3[] vertices;
    private Vector3[] normals;
    private Color[] colors;
    private int[] indices;
    private GenericLidarSensor lidar;
    private bool firstTime = true;
    private float intensity, range;
    private Vector3 normal;
    private int id;
    private int numBeams;
    public int numRays;
    public List<PointCollection> Points;
    private int numSnapRays;
    private int numPointsPerSnap;
    private List<int> scanIndex;
    private bool outputData = false;
    private float initialCamRot;
    private float currentT;
    private float previousT;
    private bool isInitialized = false;
    private int scanId = 0;
    TimeSpan startTime = TimeSpan.Zero;  
    private static readonly byte[] sixbytegap = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    private static readonly byte[] fourbytegap = { 0x00, 0x00, 0x00, 0x00 };
    

    // Use this for initialization
    IEnumerator Start()
    {
        while (lidar == null)
        {
            lidar = GetComponentInChildren<GenericLidarSensor>();
            if (lidar == null)
            {
                yield return new WaitForFixedUpdate();
            }
            else
            {
                if (lidar.isInitialized == false)
                {
                    lidar = null;
                    yield return new WaitForFixedUpdate();
                }
            }
        }
        numBeams = lidar.VerticalBeams;
        numRays = lidar.TotalNumberOfData;
        numSnapRays = lidar.NumberOfSnapRays;
        numPointsPerSnap = lidar.NumberOfPointsPerSnap;
        scanIndex = lidar.ScanIndex;
        initialCamRot = lidar.InitialCameraRotation;

        vertices = new Vector3[numRays];
        normals = new Vector3[numRays];
        colors = new Color[numRays];
        indices = new int[numRays];

        MeshFilter mf = this.gameObject.AddComponent<MeshFilter>();
        pointMesh = mf.GetComponent<MeshFilter>().mesh;

        MeshRenderer mr = this.gameObject.AddComponent<MeshRenderer>();
        mr.material = meshMaterial;
        mr.enabled = on;

        pointMesh.bounds = new Bounds(new Vector3(0, 0, 0), new Vector3(lidar.SensorRange, 1, lidar.SensorRange));

        for (int n = 0; n < vertices.Length; n++)
            indices[n] = n;

        currentT = Time.fixedTime;
        previousT = currentT;

        Points = new List<PointCollection>();

        Configure();

        isInitialized = true;
    }

    public void Activate(bool isOn)
    {
        MeshRenderer mr = this.gameObject.GetComponent<MeshRenderer>();
        mr.enabled = isOn;
        on = isOn;
    }

    void OnDrawGizmos()
    {
        if (vertices == null)
            return;

        Gizmos.color = Color.red;
        for (int n = 0; n < vertices.Length; ++n)
        {
            var prev = vertices[n];

            Gizmos.color = Color.red;
            Gizmos.DrawLine(this.transform.TransformPoint(prev), this.transform.TransformPoint(prev) + 0.05f * normals[n]);

        }
    }

    public void Configure()
    {

        firstTime = true;

        if (pointMesh != null)
        {
            pointMesh.Clear();
        }

        vertices = new Vector3[numRays];
        normals = new Vector3[numRays];
        colors = new Color[numRays];
        indices = new int[numRays];

        pointMesh.bounds = new Bounds(new Vector3(0, 0, 0), new Vector3(lidar.SensorRange, 1, lidar.SensorRange));

        for (int n = 0; n < vertices.Length; n++)
            indices[n] = n;
    }

    private float nextUpdate;

    // Update is called once per frame
    void FixedUpdate()
    {
        if (!isInitialized)
            return;
        currentT = UnityEngine.Time.fixedTime;
        if (currentT >= nextUpdate)
        {
            nextUpdate = currentT + Math.Max(0, Period - 0.0075f);

            UpdatePoints();
 
        }
    }
    
    public void UpdatePoints()
    {
        int yawBins = Mathf.CeilToInt(numPointsPerSnap / lidar.VerticalBeams);
        if (lidar.LidarUpdate())
        {
            // reset points
            Points.Clear();
            for (int ray = 0; ray < numRays; ray++)
            {
                float range;
                float intensity;
                int reflectivity;
                int id;
                Vector3 norm;

                //int index = scanIndex[ray];
                int rows = (int)MathF.Floor(ray / lidar.HorizontalBeams);
                int cols = lidar.HorizontalBeams - (ray - lidar.HorizontalBeams * rows);
                int snap = (int)MathF.Floor(cols / yawBins);
                int scol = cols - yawBins * snap;
                int pointIndex = rows * yawBins + scol;

                // check if range is within ranges
                lidar.GetRangeAndIntensityAndIdAndNormal((int)ray, out range, out intensity, out id, out norm,
                    out reflectivity);

                // process for each 
                if (0.1 <= range && range <= lidar.SensorRange)
                {
                    //int snap = Mathf.FloorToInt(ray / numPointsPerSnap);
                    //int pointIndex = (int)ray - (int)snap * (int)numPointsPerSnap;

                    // get xyz coord based on range / rho
                    //Debug.Log(pointIndex);
                    var xz = lidar.XZLookUp[pointIndex];
                    var uv = lidar.UVLookUp[pointIndex];


                    float rho = Mathf.Sqrt(xz.x * xz.x + 1.0f + xz.y * xz.y);
                    float fac = range / rho;

                    float yaw = snap * lidar.SnapHorizontalFieldOfView - initialCamRot;
                    UnityEngine.Quaternion rot = UnityEngine.Quaternion.Euler(new Vector3(0, -yaw, 0));

                    float delta = UnityEngine.Random.Range(-0.001f, 0.001f);
                    var point = new Vector3(xz.x * fac, xz.y * fac, fac); // + new Vector3(delta, delta, delta);
                    point = rot * point;

                    vertices[ray] = point;
                    normals[ray] = norm;

                    Color pColor = new Color();

                    if (showSegmentationColors)
                    {
                        int colorID = id % idColors.Length;
                        pColor = idColors[colorID];
                        //colors[ray] = pColor;
                    }
                    else
                        colors[ray] = Color.white * intensity;

                    var EP = point;
                    //var WP = this.transform.TransformPoint(EP);
                    //writer.WriteLine($"{ray},{range},{EP.x},{EP.z},{EP.y}");

                    int channel = (int)xz.z;


                    Points.Add(new PointCollection(id - 2,
                        new Vector3(point.x, point.y, point.z),
                        range,
                        intensity,
                        reflectivity,
                        channel, xz));

                }
                else
                {
                    vertices[ray] = Vector3.zero;
                    normals[ray] = Vector3.zero;
                }
            }


            // loop through each point


            PublishHandler?.Invoke(this, EventArgs.Empty);

            pointMesh.vertices = vertices;
            pointMesh.colors = colors;
            pointMesh.normals = normals;
            pointMesh.RecalculateBounds();


            if (firstTime)
            {
                pointMesh.SetIndices(indices, MeshTopology.Points, 0);
                firstTime = false;
            }
        }
    }
#if false
    PointCloud2Msg UpdatedLidarData()
    {
        // var node = new NodeHandle();
        var lidardata = new PointCloud2Msg();
        lidardata.header = new HeaderMsg();
        lidardata.header.frame_id = "map";
        lidardata.width  = (uint)lidar.NumberOfPointsInARing;
        lidardata.height = (uint)lidar.VerticalBeams;


        // define the fields
        lidardata.fields = new PointFieldMsg[10];

        var index = 0;
        lidardata.fields[index] = new PointFieldMsg();
        lidardata.fields[index].name = "timestamp";
        lidardata.fields[index].count = 1;
        lidardata.fields[index].offset = 0;
        lidardata.fields[index].datatype = 8; // uint_64

        index++;
        lidardata.fields[index] = new PointFieldMsg();
        lidardata.fields[index].name = "image_x";
        lidardata.fields[index].count = 1;
        lidardata.fields[index].offset = 8;
        lidardata.fields[index].datatype = 7; // float

        index++;
        lidardata.fields[index] = new PointFieldMsg();
        lidardata.fields[index].name = "distance";
        lidardata.fields[index].count = 1;
        lidardata.fields[index].offset = 12;
        lidardata.fields[index].datatype = 7; // float

        index++;
        lidardata.fields[index] = new PointFieldMsg();
        lidardata.fields[index].name = "image_z";
        lidardata.fields[index].count = 1;
        lidardata.fields[index].offset = 16;
        lidardata.fields[index].datatype = 7; // float

        index++;
        lidardata.fields[index] = new PointFieldMsg();
        lidardata.fields[index].name = "intensity";
        lidardata.fields[index].count = 1;
        lidardata.fields[index].offset = 20;
        lidardata.fields[index].datatype = 7; // float

        index++;
        lidardata.fields[index] = new PointFieldMsg();
        lidardata.fields[index].name = "return_type";
        lidardata.fields[index].count = 1;
        lidardata.fields[index].offset = 24;
        lidardata.fields[index].datatype = 2; // byte

        index++;
        lidardata.fields[index] = new PointFieldMsg();
        lidardata.fields[index].name = "flags";
        lidardata.fields[index].count = 1;
        lidardata.fields[index].offset = 25;
        lidardata.fields[index].datatype = 2; // byte

        index++;
        lidardata.fields[index] = new PointFieldMsg();
        lidardata.fields[index].name = "x";
        lidardata.fields[index].count = 1;
        lidardata.fields[index].offset = 32;
        lidardata.fields[index].datatype = 7; // float

        index++;
        lidardata.fields[index] = new PointFieldMsg();
        lidardata.fields[index].name = "y";
        lidardata.fields[index].count = 1;
        lidardata.fields[index].offset = 36;
        lidardata.fields[index].datatype = 7; // float

        index++;
        lidardata.fields[index] = new PointFieldMsg();
        lidardata.fields[index].name = "z";
        lidardata.fields[index].count = 1;
        lidardata.fields[index].offset = 40;
        lidardata.fields[index].datatype = 7; // float

        lidardata.is_bigendian = false;
        lidardata.point_step = 48; // sizeof(long)  timestamp 
                               // sizeof(float) image_x 
                               // sizeof(float) distance 
                               // sizeof(float) image_z
                               // sizeof(float) intensity 
                               // sizeof(byte)  return_type 
                               // sizeof(byte)  flags
                               // 6 byte gap 
                               // sizeof(float) x
                               // sizeof(float) y 
                               // sizeof(float) z
                               // 4 byte gap
        int numBeamRays = lidar.NumberOfPointsInARing;

        // else if (_ceptonVistaSensor != null)
        //     numBeamRays = 300;
        lidardata.row_step = (uint) numBeamRays * lidardata.point_step;
        lidardata.data = new byte[lidardata.row_step * lidardata.height];
        lidardata.is_dense = true;

        return lidardata;
    }
    void PublishLidar()
    {
        int yawBins = Mathf.CeilToInt(numPointsPerSnap / lidar.VerticalBeams);
        const byte returnType = 0x03;
        const byte flags = 0x01;
        var lidardata = UpdatedLidarData();
        if (lidar.LidarUpdate())
        {
            using (var ms = new MemoryStream(lidardata.data))
            {
                using (var br = new BinaryWriter(ms))
                {
                    // reset points
                    Points.Clear();
                    var StartTime = DateTime.Now.Subtract(new DateTime(1970, 1, 1, 0, 0, 0));
                    var time = Time.fixedTime;
                    // var stamp = Uml.Robotics.Ros.ROS.GetTime<Messages.std_msgs.Time>(StartTime + TimeSpan.FromSeconds(time));

                    

                    var now = StartTime + TimeSpan.FromSeconds(time);
                    //lidar.header.stamp = ROS.GetTime<Messages.std_msgs.Time>(now);
                    _stamp = new TimeMsg();
                    _stamp.sec = StartTime.Seconds;
                    lidardata.header.stamp = _stamp;

                    var ts = (long)(now.TotalMilliseconds * 1000);
                    
                    for (int ray = 0; ray < numRays; ray++)
                    {
                        float range;
                        float intensity;
                        int reflectivity;
                        int id;
                        Vector3 norm;

                        //int index = scanIndex[ray];
                        int rows = (int)MathF.Floor(ray / lidar.HorizontalBeams);
                        int cols = lidar.HorizontalBeams - (ray - lidar.HorizontalBeams * rows);
                        int snap = (int)MathF.Floor(cols / yawBins);
                        int scol = cols - yawBins * snap;
                        int pointIndex = rows * yawBins + scol;

                        // check if range is within ranges
                        lidar.GetRangeAndIntensityAndIdAndNormal((int)ray, out range, out intensity, out id, out norm,
                            out reflectivity);

                        // process for each 
                        if (0.1 <= range && range <= lidar.SensorRange)
                        {
                            //int snap = Mathf.FloorToInt(ray / numPointsPerSnap);
                            //int pointIndex = (int)ray - (int)snap * (int)numPointsPerSnap;

                            // get xyz coord based on range / rho
                            //Debug.Log(pointIndex);
                            var xz = lidar.XZLookUp[pointIndex];
                            var uv = lidar.UVLookUp[pointIndex];


                            float rho = Mathf.Sqrt(xz.x * xz.x + 1.0f + xz.y * xz.y);
                            float fac = range / rho;

                            float yaw = snap * lidar.SnapHorizontalFieldOfView - initialCamRot;
                            UnityEngine.Quaternion rot = UnityEngine.Quaternion.Euler(new Vector3(0, -yaw, 0));

                            float delta = UnityEngine.Random.Range(-0.001f, 0.001f);
                            var point = new Vector3(xz.x * fac, xz.y * fac, fac); // + new Vector3(delta, delta, delta);
                            point = rot * point;
                            
                            var xyz = new Vector3(point.z, -point.x, point.y);
                            br.Write(ts);
                            br.Write(-xz.x);
                            br.Write(range);
                            br.Write(-xz.y);
                            br.Write(intensity);
                            br.Write(returnType);
                            br.Write(flags);
                            br.Write(sixbytegap);
                            br.Write(xyz.x);
                            br.Write(xyz.y);
                            br.Write(xyz.z);
                            br.Write(fourbytegap);

                            vertices[ray] = point;
                            normals[ray] = norm;

                            Color pColor = new Color();

                            if (showSegmentationColors)
                            {
                                int colorID = id % idColors.Length;
                                pColor = idColors[colorID];
                                //colors[ray] = pColor;
                            }
                            else
                                colors[ray] = Color.white * intensity;

                            var EP = point;
                            //var WP = this.transform.TransformPoint(EP);
                            //writer.WriteLine($"{ray},{range},{EP.x},{EP.z},{EP.y}");

                            int channel = (int)xz.z;


                            Points.Add(new PointCollection(id - 2,
                                new Vector3(point.x, point.y, point.z),
                                range,
                                intensity,
                                reflectivity,
                                channel, xz));
                            
                            

                        }
                        else
                        {
                            vertices[ray] = Vector3.zero;
                            normals[ray] = Vector3.zero;
                        }
                    }

                }
            }
            // loop through each point


            ros.Publish(pointCloudTopicName, lidardata);
            PublishFrame();

            pointMesh.vertices = vertices;
            pointMesh.colors = colors;
            pointMesh.normals = normals;
            pointMesh.RecalculateBounds();


            if (firstTime)
            {
                pointMesh.SetIndices(indices, MeshTopology.Points, 0);
                firstTime = false;
            }
        }
    }

    public void PublishFrame()
    {
        var tfFrame = new TFMessageMsg() {transforms = new TransformStampedMsg[1]};
        tfFrame.transforms[0] = _thisFrame.Frame;
        tfFrame.transforms[0].header = new HeaderMsg();
        tfFrame.transforms[0].header.stamp = _stamp;
        
        ros.Publish(frameTopicName, tfFrame);
    }
#endif
}

public struct PointCollection
{
    public int id;
    public Vector3 point;
    public float range;
    public float intensity;
    public float reflectivity;
    public int channel;
    public Vector3 Xz;

    public PointCollection(int I, Vector3 P, float R, float Intensity, float Reflectivity, int Channel, Vector3 xz)
    {
        id = I;
        point = P;
        range = R;
        intensity = Intensity;
        reflectivity = Reflectivity;
        channel = Channel;
        Xz = xz;
    }
}
