 using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

[System.Serializable]
public class AgentData
{
    public string id;
    public Position position;
}

[System.Serializable]
public class Position
{
    public float x;
    public float y;
}

public class TCPIPServerAsync : MonoBehaviour
{
    // Use this for initialization
    System.Threading.Thread SocketThread;
    volatile bool keepReading = false;
    
    // Dictionary to store agent cubes
    private Dictionary<string, GameObject> agentCubes = new Dictionary<string, GameObject>();
    
    // Queue for thread-safe communication between network thread and main thread
    private Queue<AgentData> agentDataQueue = new Queue<AgentData>();
    private readonly object queueLock = new object();
    
    // Cube prefab settings
    public Material cubeMaterial;
    public float cubeSize = 1.0f;
    public float heightOffset = 0.0f; // Y position for the cubes
    
    void Start()
    {
        Application.runInBackground = true;
        
        // Create a default material if none assigned
        if (cubeMaterial == null)
        {
            cubeMaterial = new Material(Shader.Find("Standard"));
            cubeMaterial.color = Color.blue;
        }
        
        startServer();
    }
    
    void Update()
    {
        // Process agent data from the queue in the main thread
        lock (queueLock)
        {
            while (agentDataQueue.Count > 0)
            {
                AgentData data = agentDataQueue.Dequeue();
                UpdateAgentCube(data);
            }
        }
    }
    
    void UpdateAgentCube(AgentData data)
    {
        GameObject cube;
        
        if (agentCubes.ContainsKey(data.id))
        {
            cube = agentCubes[data.id];
        }
        else
        {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = data.id;
            cube.transform.localScale = Vector3.one * cubeSize;
            
            // Apply material
            Renderer renderer = cube.GetComponent<Renderer>();
            renderer.material = cubeMaterial;
            
            Color randomColor = new Color(
                UnityEngine.Random.Range(0.3f, 1.0f),
                UnityEngine.Random.Range(0.3f, 1.0f),
                UnityEngine.Random.Range(0.3f, 1.0f)
            );
            renderer.material.color = randomColor;
            
            agentCubes[data.id] = cube;
            Debug.Log($"Created cube for {data.id}");
        }
        
        Vector3 newPosition = new Vector3(data.position.x, heightOffset, data.position.y);
        cube.transform.position = newPosition;
    }
    
    void startServer()
    {
        SocketThread = new System.Threading.Thread(networkCode);
        SocketThread.IsBackground = true;
        SocketThread.Start();
    }
    
    private string getIPAddress()
    {
        IPHostEntry host;
        string localIP = "";
        host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (IPAddress ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                localIP = ip.ToString();
            }
        }
        return localIP;
    }
    
    Socket listener;
    Socket handler;
    
    void networkCode()
    {
        string data;
        // Data buffer for incoming data.
        byte[] bytes = new Byte[1024];
        
        // host running the application.
        // Create EndPoint
        IPAddress IPAdr = IPAddress.Parse("127.0.0.1"); // Dirección IP
        IPEndPoint localEndPoint = new IPEndPoint(IPAdr, 1114);
        
        // Create a TCP/IP socket.
        listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        
        // Bind the socket to the local endpoint and
        // listen for incoming connections.
        try
        {
            listener.Bind(localEndPoint);
            listener.Listen(10);
            
            // Start listening for connections.
            while (true)
            {
                keepReading = true;
                // Program is suspended while waiting for an incoming connection.
                Debug.Log("Waiting for Connection");
                handler = listener.Accept();
                Debug.Log("Client Connected");
                
                data = "";
                byte[] SendBytes = System.Text.Encoding.Default.GetBytes("I will send key");
                handler.Send(SendBytes); // dar al cliente
                
                // An incoming connection needs to be processed.
                while (keepReading)
                {
                    bytes = new byte[1024];
                    int bytesRec = handler.Receive(bytes);
                    if (bytesRec <= 0)
                    {
                        keepReading = false;
                        handler.Disconnect(true);
                        break;
                    }
                    
                    data += System.Text.Encoding.ASCII.GetString(bytes, 0, bytesRec);
                    Debug.Log("Received from Client: " + data);
                    
                    if (data.IndexOf("$") > -1)
                    {
                        // Remove the delimiter and process the JSON
                        string jsonData = data.Replace("$", "");
                        ProcessAgentData(jsonData);
                        data = ""; // Reset data for next message
                        break;
                    }
                    System.Threading.Thread.Sleep(1);
                }
                System.Threading.Thread.Sleep(1);
            }
        }
        catch (Exception e)
        {
            Debug.Log(e.ToString());
        }
    }
    
    void ProcessAgentData(string jsonData)
    {
        try
        {
            AgentData agentData = JsonUtility.FromJson<AgentData>(jsonData);
            
            // Add to queue for processing in main thread
            lock (queueLock)
            {
                agentDataQueue.Enqueue(agentData);
            }
            
            Debug.Log($"Processed data for {agentData.id}: ({agentData.position.x}, {agentData.position.y})");
        }
        catch (Exception e)
        {
            Debug.LogError("Error parsing JSON: " + e.Message);
            Debug.LogError("JSON data: " + jsonData);
        }
    }
    
    void stopServer()
    {
        keepReading = false;
        // stop thread
        if (SocketThread != null)
        {
            SocketThread.Abort();
        }
        if (handler != null && handler.Connected)
        {
            handler.Disconnect(false);
            Debug.Log("Disconnected!");
        }
    }
    
    void OnDisable()
    {
        stopServer();
    }
    
    void OnDestroy()
    {
        // Clean up created cubes
        foreach (var cube in agentCubes.Values)
        {
            if (cube != null)
            {
                Destroy(cube);
            }
        }
        agentCubes.Clear();
    }
}