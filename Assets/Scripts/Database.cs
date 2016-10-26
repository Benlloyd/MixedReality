using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using HoloToolkit.Unity;
using UnityEngine.VR;

public class Database : SpatialMappingSource {
    /// <summary>
    /// Type of devices that this software can run on.
    /// </summary>
    public enum DeviceType
    {
        MasterClient,
        HoloLens,
        Vive,
        Kinect,
        Desktop
    }

    public static DeviceType Type;
    private static byte[] _mesh;
    private static int _currentMeshVersion = 0;
    private static int _lastestMeshVersion = 0;

    public SpatialMappingManager SpatialMappingManager;
    public static SpatialMappingManager MappingManager;

    protected override void Awake()
    {
        base.Awake();
        MappingManager = SpatialMappingManager;
        DetactDeviceType();
    }

    void Update()
    {
        if (Type == DeviceType.MasterClient)
        {
            if (Input.GetKeyDown("l"))
            {
                DisplayMeshes();
            }
        }
        else if (Type == DeviceType.HoloLens)
        {
            
        }
        else
        {
            if (_currentMeshVersion != _lastestMeshVersion)
            {
                DisplayMeshes();
                _currentMeshVersion = _lastestMeshVersion;
            }
        }
    }

    /// <summary>
    /// This method will detect which <see cref="DeviceType"/> is the software currently running on. 
    /// <see cref="DeviceType.Kinect"/> is currently not working. It will be detacted as desktop.
    /// </summary>
    private static void DetactDeviceType()
    {
#if (UNITY_STANDALONE_WIN && !UNITY_EDITOR) && !UNITY_WSA_10_0
        List<string> supportedDeviceList = new List<string>(VRSettings.supportedDevices);
        if (supportedDeviceList.Contains("OpenVR")) {
            Type = DeviceType.Vive;
        }
        return;
#elif !UNITY_EDITOR && UNITY_WSA_10_0
        List<string> supportedDeviceList = new List<string>(VRSettings.supportedDevices);
        if (supportedDeviceList.Contains("HoloLens")) {
            Type = DeviceType.HoloLens;
            //MappingManager.StartObserver();
            Debug.Log("Start Observing");
        }
#else
        Type = DeviceType.Desktop;
#endif
    }

    public static byte[] GetMeshAsBytes()
    {
        return _mesh;
    }

    public static IEnumerable<Mesh> GetMeshAsEnumerable()
    {
        return SimpleMeshSerializer.Deserialize(_mesh);
    }

    public static void UpdateMesh(byte[] newMesh)
    {
        _mesh = newMesh;
        _lastestMeshVersion++;
    }

    public static void UpdateMesh(IEnumerable<Mesh> newMesh)
    {
        _mesh = SimpleMeshSerializer.Serialize(newMesh);
        _lastestMeshVersion++;
    }

    public static void LoadMesh()
    {
        // Master client will load mesh from disk
        if (Type == DeviceType.MasterClient)
        {
            _mesh = SimpleMeshSerializer.Serialize(MeshSaver.Load("RoomMesh"));
        }
        // HoloLens will scan the room and creates mesh
        else if (Type == DeviceType.HoloLens)
        {
            List<Mesh> meshes = MappingManager.GetMeshes();
            _mesh = SimpleMeshSerializer.Serialize(meshes);
        }
        Debug.Log("Meshes loaded");
    }

    public static void SaveMesh()
    {
        if (Type == DeviceType.MasterClient)
        {
            MeshSaver.Save("RoomMesh", SimpleMeshSerializer.Deserialize(_mesh));
        }
    }

    public void DisplayMeshes()
    {
        var meshes = SimpleMeshSerializer.Deserialize(_mesh);
        Debug.Log(meshes.Count());
        foreach (var mesh in meshes) {
            GameObject surface = AddSurfaceObject(mesh, string.Format("Beamed-{0}", SurfaceObjects.Count), transform);
            surface.transform.parent = SpatialMappingManager.Instance.transform;
            surface.GetComponent<MeshRenderer>().enabled = true;
            surface.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }
}
