using Photon;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Net.Sockets;
using Windows.Foundation;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;


public class Launcher : PunBehaviour
{
#if UNITY_UWP
    // Connection from Hololens to Masterclient for sending mesh.
    private StreamSocket holoLensClient;
    private IAsyncAction connection;
#endif

    [Tooltip("Max player that can be in a room")] private static byte MaxPlayer = 4;
    [Tooltip("Game version number")] private static string Version = "1";
    [Tooltip("Port number that server sits on")] public int Port = 25827;

    void Awake() {

        Debug.Log("Laucher awaken");

        PhotonNetwork.logLevel = PhotonLogLevel.Full;

        PhotonNetwork.autoJoinLobby = false;

        PhotonNetwork.automaticallySyncScene = true;
    }

    // Use this for initialization
    void Start () {
	    Connect();
        Debug.Log(Database.Type);
	}

    public override void OnPhotonJoinRoomFailed(object[] codeAndMsg)
    {
        if (Database.Type == Database.DeviceType.Desktop) {
            Debug.Log("Creates new room and becomes the master client");
            Database.Type = Database.DeviceType.MasterClient;
            PhotonNetwork.CreateRoom("UWBMixedReality", new RoomOptions() { MaxPlayers = MaxPlayer }, null);
        } else {
            Debug.Log("Server not found. Disconnect from PUN");
            PhotonNetwork.Disconnect();
        }
    }

    /// <summary>
    /// Setting up a TCP server and listen on port sepecify earlier in the code. Once a connection comes in, start a new thread that send mesh to the connected client.
    /// </summary>

#if !UNITY_UWP
    public override void OnCreatedRoom()
    {
        var server = new TcpListener(IPAddress.Any, Port);
        server.Start();
        server.BeginAcceptTcpClient(DoAcceptTcpClient, server);
    }
#endif

    public override void OnConnectedToMaster()
    {
        PhotonNetwork.JoinRoom("UWBMixedReality");
    }


    public override void OnJoinedRoom()
    {
        if (Database.Type == Database.DeviceType.MasterClient)
        {
            Database.LoadMesh();
        }
        // Scan and send mesh to master client if you are HoloLens
        else if (Database.Type == Database.DeviceType.HoloLens)
        {
            Debug.Log("OnJoinesRoom called by Hololens");
            Database.LoadMesh();

            photonView.RPC("UpdateMesh", PhotonTargets.MasterClient, PhotonNetwork.player.ID);
        }
        // Connect to master client and receive meshes
        else
        {
            photonView.RPC("SendMesh", PhotonTargets.MasterClient, PhotonNetwork.player.ID);
        }
    }

    [PunRPC]
    public void SendMesh(int id)
    {
        if (Database.Type == Database.DeviceType.MasterClient) 
        {
            Debug.Log(GetLocalIpAddress() + ":" + Port);
            photonView.RPC("UpdateMesh", PhotonPlayer.Find(id), GetLocalIpAddress() + ":" + Port);
        }
    }

    [PunRPC]
    public void SendMesh(string networkConfig)
    {
        if (Database.Type == Database.DeviceType.HoloLens)
        {
            holoLensClient = new StreamSocket();
            var networkConfigArray = networkConfig.Split(':');
            connection = holoLensClient.ConnectAsync(new HostName(networkConfigArray[0]), networkConfigArray[1]);
            var aach = new AsyncActionCompletedHandler(NetworkConnectedHandler);
            connection.Completed = aach;
        }
    }

    public void NetworkConnectedHandler(IAsyncAction asyncInfo, AsyncStatus status) {
        //Debug.Log("YOU CONNECTED TO: " + networkConnection.Information.RemoteAddress.ToString());

        // Status completed is successful.
        if (status == AsyncStatus.Completed) {
            //Debug.Log("PREPARING TO WRITE DATA...");

            DataWriter networkDataWriter;

            // Since we are connected, we can send the data we set aside when establishing the connection.
            using (networkDataWriter = new DataWriter(holoLensClient.OutputStream)) {
                networkDataWriter.WriteBytes(Database.GetMeshAsBytes());
            }
        } else {
            //Debug.Log("Failed to establish connection. Error Code: " + asyncInfo.ErrorCode);
            // In the failure case we'll requeue the data and wait before trying again.
            holoLensClient.Dispose();
        }
    }

    /// <summary>
    /// Update mesh in database
    /// </summary>
    [PunRPC]
    public void UpdateMesh(string networkConfig) {
        // Master client will update its own mesh and push them to all client
        if (Database.Type == Database.DeviceType.MasterClient) {
#if !UNITY_UWP
            ReceiveMeshFromNetworkConfig(networkConfig);
            photonView.RPC("UpdateMesh", PhotonTargets.Others, GetLocalIpAddress() + ":" + Port);
#endif
        }
        // HoloLens client will do nothing
        else if (Database.Type == Database.DeviceType.HoloLens) {

        }
        // Otherwise, connect to server and receive mesh
        else {
#if !UNITY_UWP
            ReceiveMeshFromNetworkConfig(networkConfig);
#endif
        }
    }


    [PunRPC]
    public void UpdateMesh(int id)
    {
#if !UNITY_UWP
        var meshReceiver = new TcpListener(IPAddress.Any, Port);
        meshReceiver.Start();
        meshReceiver.BeginAcceptTcpClient(DoAcceptTcpClientForReceivingMesh);
        photonView.RPC("SendMesh", PhotonPlayer.Find(id), GetLocalIpAddress() + ":" + Port)
#endif
    }



    /// <summary>
    /// This method will connect this client to PUN Server
    /// </summary>
    private void Connect()
    {
        if (PhotonNetwork.connected)
        {
            PhotonNetwork.JoinRoom("UWBMixedReality");
        } 
        else
        {
            PhotonNetwork.ConnectUsingSettings(Version);
        }
    }

    /// <summary>
    /// Get local IP address
    /// </summary>
    /// <returns>Local IP address</returns>

    private IPAddress GetLocalIpAddress()
    {
        var icp = NetworkInformation.GetInternetConnectionProfile();

        if (icp?.NetworkAdapter == null) return null;
        var hostname =
            NetworkInformation.GetHostNames()
                .SingleOrDefault(
                    hn =>
                        hn.IPInformation?.NetworkAdapter != null && hn.IPInformation.NetworkAdapter.NetworkAdapterId
                        == icp.NetworkAdapter.NetworkAdapterId);

        // the ip address
        return IPAddress.Parse(hostname?.ToString());
    }


#if !UNITY_EDITOR && !UNITY_WSA_10_0
    private void ReceiveMeshFromNetworkConfig(string networkConfig)
    {
        var serverIpConfig = networkConfig.Split(':');
        IPAddress ipAddress = IPAddress.Parse(serverIpConfig[0]);
        int port = int.Parse(serverIpConfig[1]);
        TcpClient client = new TcpClient();
        client.Connect(ipAddress, port);

        if (!client.Connected)
        {
            Debug.Log("Cannot connect to server");
        }

        using (var networkStream = client.GetStream()) {
            byte[] data = new byte[1024];

            Debug.Log("Start receiving mesh");
            using (MemoryStream ms = new MemoryStream()) {
                int numBytesRead;
                while ((numBytesRead = networkStream.Read(data, 0, data.Length)) > 0) {
                    ms.Write(data, 0, numBytesRead);
                }
                Debug.Log("finish receiving mesh: size = " + ms.Length);
                client.Close();
                Database.UpdateMesh(ms.ToArray());
            }
        }
    }
#endif


#if !UNITY_UWP
    private void DoAcceptTcpClient(IAsyncResult result)
    {
        var server = (TcpListener) result.AsyncState;
        TcpClient client = server.EndAcceptTcpClient(result);
        server.BeginAcceptTcpClient(DoAcceptTcpClient, server);
        Debug.Log("Connection Accepted");
        var steam = client.GetStream();
        steam.Write(Database.GetMeshAsBytes(), 0, Database.GetMeshAsBytes().Length);
        client.Close();
    }
#endif

#if !UNITY_UWP
    private void DoAcceptTcpClientForReceivingMesh(IAsyncResult result)
    {
        var server = (TcpListener) result.AsyncState;
        TcpClient client = server.EndAcceptTcpClient(result);
        Debug.Log("Connection Accepted");
        var steam = client.GetStream();
        steam.Write(Database.GetMeshAsBytes(), 0, Database.GetMeshAsBytes().Length);
        client.Close();
    }
#endif
}
