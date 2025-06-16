using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Linq;

[System.Serializable]
public class AgentData
{
    public string id;
    public string type;
    public Position position;
    public string color;
}

[System.Serializable]
public class Position
{
    public float x;
    public float y;
}

[System.Serializable]
public class SimulationData
{
    public AgentData[] agents;
    public int step;
    public string trafficLightState;
    public int obstaclesRemoved;
}

public class TCPIPServerAsync : MonoBehaviour
{
    public int port = 1116;
    public string ipAddress = "127.0.0.1";
    
    public Material pedestrianMaterial;
    public Material vehicleMaterial;
    public Material authorityMaterial;
    public Material obstacleMaterial;
    
    public float agentScale = 0.4f;
    public float heightOffset = 0.0f;
    
    private Dictionary<string, GameObject> agentObjects = new Dictionary<string, GameObject>();
    private Queue<SimulationData> dataQueue = new Queue<SimulationData>();
    private readonly object queueLock = new object();
    
    public UnityEngine.UI.Text stepText;
    public UnityEngine.UI.Text trafficLightText;
    public UnityEngine.UI.Text obstaclesText;
    
    private GameObject[] loadedCarPrefabs;
    private GameObject[] loadedObstaclePrefabs;
    private GameObject copPrefab;
    private GameObject pedestrianPrefab;
    
    private Thread SocketThread;
    private volatile bool keepReading = false;
    private Socket listener;
    private Socket handler;

    private float boundary = 25f;

    void Start()
    {
        Application.runInBackground = true;
        CreateDefaultMaterials();
        LoadAllPrefabs();
        StartServer();
    }

    void LoadAllPrefabs()
    {
        loadedCarPrefabs = Resources.LoadAll<GameObject>("CarPrefabs");
        loadedObstaclePrefabs = Resources.LoadAll<GameObject>("ObstaclePrefabs");
        copPrefab = Resources.Load<GameObject>("cop");
        pedestrianPrefab = Resources.Load<GameObject>("PersonModel");
    }

    void CreateDefaultMaterials()
    {
        if (pedestrianMaterial == null)
        {
            pedestrianMaterial = new Material(Shader.Find("Standard"));
            pedestrianMaterial.color = Color.blue;
        }
        
        if (vehicleMaterial == null)
        {
            vehicleMaterial = new Material(Shader.Find("Standard"));
            vehicleMaterial.color = Color.red;
        }
        
        if (authorityMaterial == null)
        {
            authorityMaterial = new Material(Shader.Find("Standard"));
            authorityMaterial.color = Color.green;
        }
        
        if (obstacleMaterial == null)
        {
            obstacleMaterial = new Material(Shader.Find("Standard"));
            obstacleMaterial.color = new Color(0.6f, 0.3f, 0.1f);
        }
    }

    void Update()
    {
        lock (queueLock)
        {
            while (dataQueue.Count > 0)
            {
                SimulationData data = dataQueue.Dequeue();
                ProcessSimulationData(data);
            }
        }

        CheckVehicleBoundaries();
    }

    void CheckVehicleBoundaries()
    {
        List<string> vehiclesToRemove = new List<string>();
        
        foreach (var pair in agentObjects)
        {
            if (pair.Value != null && pair.Value.name.Contains("vehicle"))
            {
                Vector3 position = pair.Value.transform.position;
                if (Mathf.Abs(position.x) > boundary || Mathf.Abs(position.z) > boundary)
                {
                    vehiclesToRemove.Add(pair.Key);
                }
            }
        }

        foreach (string vehicleId in vehiclesToRemove)
        {
            if (agentObjects.ContainsKey(vehicleId))
            {
                Destroy(agentObjects[vehicleId]);
                agentObjects.Remove(vehicleId);
            }
        }
    }

    void ProcessSimulationData(SimulationData data)
    {
        if (stepText != null) stepText.text = "Paso: " + data.step;
        if (trafficLightText != null) 
            trafficLightText.text = data.trafficLightState == "horizontal_green" ? "HORIZONTALES VERDE" : "VERTICALES VERDE";
        if (obstaclesText != null) obstaclesText.text = "Obstáculos: " + data.obstaclesRemoved;

        HashSet<string> currentAgentIds = new HashSet<string>(data.agents.Select(a => a.id));

        List<string> agentsToRemove = new List<string>();
        foreach (var agentId in agentObjects.Keys)
        {
            if (!currentAgentIds.Contains(agentId))
            {
                Destroy(agentObjects[agentId]);
                agentsToRemove.Add(agentId);
            }
        }
        foreach (var agentId in agentsToRemove)
        {
            agentObjects.Remove(agentId);
        }

        foreach (AgentData agentData in data.agents)
        {
            UpdateOrCreateAgent(agentData);
        }
    }

    void UpdateOrCreateAgent(AgentData agentData)
    {
        GameObject agentObject;
        bool isNew = false;
        Vector3 newPosition = new Vector3(agentData.position.x, heightOffset, agentData.position.y);

        if (agentObjects.ContainsKey(agentData.id))
        {
            agentObject = agentObjects[agentData.id];
            
            Vector3 direction = newPosition - agentObject.transform.position;
            if (direction != Vector3.zero)
            {
                agentObject.transform.rotation = Quaternion.LookRotation(direction);
            }
        }
        else
        {
            switch (agentData.type)
            {
                case "pedestrian":
                    agentObject = pedestrianPrefab != null ? 
                        Instantiate(pedestrianPrefab) : 
                        GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    agentObject.transform.localScale = Vector3.one * agentScale;
                    break;
                    
                case "vehicle":
                    if (loadedCarPrefabs != null && loadedCarPrefabs.Length > 0)
                    {
                        int randomIndex = UnityEngine.Random.Range(0, loadedCarPrefabs.Length);
                        agentObject = Instantiate(loadedCarPrefabs[randomIndex]);
                        agentObject.transform.localScale = Vector3.one * agentScale;
                    }
                    else
                    {
                        agentObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    }
                    agentObject.name = "vehicle_" + agentData.id;
                    break;
                    
                case "authority":
                    if (copPrefab != null)
                    {
                        agentObject = Instantiate(copPrefab);
                        agentObject.transform.localScale = Vector3.one * agentScale;
                    }
                    else
                    {
                        agentObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                        agentObject.transform.localScale = Vector3.one * agentScale * 1.5f;
                    }
                    break;
                    
                case "obstacle":
                    if (loadedObstaclePrefabs != null && loadedObstaclePrefabs.Length > 0)
                    {
                        int randomIndex = UnityEngine.Random.Range(0, loadedObstaclePrefabs.Length);
                        agentObject = Instantiate(loadedObstaclePrefabs[randomIndex]);
                        agentObject.transform.localScale = Vector3.one * agentScale * 1.5f;
                    }
                    else
                    {
                        agentObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    }
                    break;
                    
                default:
                    agentObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    break;
            }

            agentObject.name = agentData.id;
            agentObjects[agentData.id] = agentObject;
            isNew = true;
        }

        agentObject.transform.position = newPosition;

        if (isNew && agentObject.GetComponent<Renderer>() != null)
        {
            Material materialToUse = obstacleMaterial;
            
            switch (agentData.type)
            {
                case "pedestrian":
                    materialToUse = pedestrianMaterial;
                    break;
                case "vehicle":
                    materialToUse = vehicleMaterial;
                    break;
                case "authority":
                    materialToUse = authorityMaterial;
                    break;
            }

            if (!string.IsNullOrEmpty(agentData.color))
            {
                if (ColorUtility.TryParseHtmlString(agentData.color, out Color color))
                {
                    materialToUse = new Material(materialToUse);
                    materialToUse.color = color;
                }
            }
            agentObject.GetComponent<Renderer>().material = materialToUse;
        }
    }

    void StartServer()
    {
        SocketThread = new Thread(NetworkCode);
        SocketThread.IsBackground = true;
        SocketThread.Start();
    }

    void NetworkCode()
    {
        string data;
        byte[] bytes = new Byte[1024 * 1024];

        IPAddress ip = IPAddress.Parse(ipAddress);
        IPEndPoint localEndPoint = new IPEndPoint(ip, port);

        listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            listener.Bind(localEndPoint);
            listener.Listen(10);

            while (true)
            {
                keepReading = true;
                handler = listener.Accept();

                data = "";
                byte[] welcomeBytes = System.Text.Encoding.ASCII.GetBytes("Unity Traffic Visualization Server Ready");
                handler.Send(welcomeBytes);

                while (keepReading)
                {
                    bytes = new byte[1024 * 1024];
                    int bytesRec = handler.Receive(bytes);
                    if (bytesRec <= 0)
                    {
                        keepReading = false;
                        handler.Disconnect(true);
                        break;
                    }

                    data += System.Text.Encoding.ASCII.GetString(bytes, 0, bytesRec);
                    
                    while (data.Contains("$"))
                    {
                        int delimiterIndex = data.IndexOf("$");
                        string jsonData = data.Substring(0, delimiterIndex);
                        data = data.Substring(delimiterIndex + 1);

                        try
                        {
                            SimulationData simData = JsonUtility.FromJson<SimulationData>(jsonData);
                            lock (queueLock)
                            {
                                dataQueue.Enqueue(simData);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("Error parsing JSON: " + e.Message);
                        }
                    }
                    Thread.Sleep(1);
                }
                Thread.Sleep(1);
            }
        }
        catch (Exception e)
        {
            Debug.Log(e.ToString());
        }
    }

    void StopServer()
    {
        keepReading = false;
        if (SocketThread != null) SocketThread.Abort();
        if (handler != null && handler.Connected) handler.Disconnect(false);
    }

    void OnDisable() => StopServer();

    void OnDestroy()
    {
        foreach (var obj in agentObjects.Values) if (obj != null) Destroy(obj);
        agentObjects.Clear();
    }
}