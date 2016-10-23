using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using WebSocketSharp;

public class Launcher : Photon.PunBehaviour
{
    [Tooltip("Max player that can be in a room")] private static byte MaxPlayer = 4;
    [Tooltip("Game version number")] private static string Version = "1";
    [Tooltip("Port number that server sits on")] public int Port = 25827;

    void Awake() {
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
    public override void OnCreatedRoom()
    {
        TcpListener server = new TcpListener(IPAddress.Any, Port);
        server.Start();
        new Thread(() =>
            {
                TcpClient client = server.AcceptTcpClient();
                new Thread(() =>
                {
                    var steam = client.GetStream();
                    steam.Write(Database.GetMeshAsBytes(), 0, Database.GetMeshAsBytes().Length);
                }).Start();
            }
        ).Start();
    }

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
            Database.LoadMesh();

            TcpListener server = new TcpListener(IPAddress.Any, Port + 1);
            server.Start();
            new Thread(() =>
            {
                var client = server.AcceptTcpClient();
                var steam = client.GetStream();
                steam.Write(Database.GetMeshAsBytes(), 0, Database.GetMeshAsBytes().Length);
                client.Close();
            }).Start();
            
            photonView.RPC("UpdateMesh", PhotonTargets.MasterClient, GetLocalIpAddress() + ":" + (Port + 1));
        }
        // Connect to master client and receive meshes
        else
        {
            photonView.RPC("SendMesh", PhotonTargets.MasterClient, photonView.owner.ID);
        }
    }

    [PunRPC]
    public void SendMesh(int id)
    {
        photonView.RPC("UpdateMesh", PhotonPlayer.Find(id), GetLocalIpAddress() + ":" + Port);
    }

    /// <summary>
    /// Update mesh in database
    /// </summary>
    [PunRPC]
    public void UpdateMesh(string networkConfig)
    {
        // Master client will update its own mesh and push them to all client
        if (Database.Type == Database.DeviceType.MasterClient)
        {
            ReceiveMeshFromNetworkConfig(networkConfig);
            photonView.RPC("UpdateMesh", PhotonTargets.Others, GetLocalIpAddress() + ":" + Port);
        }
        // HoloLens client will do nothing
        else if (Database.Type == Database.DeviceType.HoloLens)
        {
            
        }
        // Otherwise, connect to server and receive mesh
        else
        {
            ReceiveMeshFromNetworkConfig(networkConfig);
        }
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
    private IPAddress GetLocalIpAddress() {
        IPAddress retVal = null;
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (IPAddress ip in host.AddressList) {
            if (ip.AddressFamily.ToString() == "InterNetwork") {
                retVal = ip;
            }
        }
        return retVal;
    }

    private void ReceiveMeshFromNetworkConfig(string networkConfig)
    {
        var serverIpConfig = networkConfig.Split(':');
        IPAddress ipAddress = IPAddress.Parse(serverIpConfig[0]);
        int port = int.Parse(serverIpConfig[1]);

        TcpClient client = new TcpClient();
        client.Connect(ipAddress, port);

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
}
