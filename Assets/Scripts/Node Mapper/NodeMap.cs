using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace LuminousVector
{
	public class NodeMap : MonoBehaviour
	{

		private static NodeMap NODE_MAP;

		public static NodeMap instance
		{
			get
			{
				if (!NODE_MAP)
				{
					NODE_MAP = FindObjectOfType<NodeMap>() as NodeMap;
					if (!NODE_MAP)
					{
						Debug.LogError("No Event Manager found");
					}
				}
				return NODE_MAP;
			}
		}

		public int nodesToGenerate = 50;
		public int maxGenerationCycles = 1000;
		public List<Node> nodes;
		public int mapHeight = 100;
		public int mapWidth = 100;
		public float minNodeDistance = 3;
		public float maxConnectionDistance = 5;
		public int minNodeConnections = 1;
		public int maxNodeConnections = 3;
		public int connectionAttemptTimeOut = 20;
		public Texture2D heightMapTexture;
		public Texture2D nodeMap;
		public int nodeMapResolution = 8;
		public RawImage renderOutput;
		public bool renderNodeMap = true;
		public bool regenerate = false;
		public Terrain terrain;

		private Color[] _colors;
		private int _w, _h;

		void Start()
		{
			Generate();
		}

		public void Generate()
		{
			GenerateTerrain();
			GenerateNodeMap();
			EventManager.TriggerEvent(GameEvent.NODE_MAP_GENERATED);
			if(renderNodeMap)
				RenderNodeMap();
		}

		void Update()
		{
			if(regenerate)
			{
				Generate();
				regenerate = false;
			}
		}

		public static List<Node> GetNodes()
		{
			return instance.nodes;
		}

		void GenerateTerrain()
		{
			TerrainGeneratorConfig config = new TerrainGeneratorConfig();
			config.width= mapWidth * nodeMapResolution;
			config.height = mapHeight * nodeMapResolution;
			config.scale = new Vector2(1, 1);
			config.origin = Vector2.zero;
			TerrainGenerator terrainGen = new TerrainGenerator(config);
			float[,] heightMap;
			terrainGen.CreateHeightMap(out heightMap);
			TerrainData terData = terrain.terrainData;
			terData.heightmapResolution = config.height;
			terData.SetHeights(0, 0, heightMap);
		}

		void RenderNodeMap()
		{
			System.DateTime startTime = System.DateTime.Now;
			Debug.Log("Rendering map");
			//Creat the nodeMap Texture
			nodeMap = new Texture2D(mapWidth * nodeMapResolution, mapHeight * nodeMapResolution, TextureFormat.RGBA32, false);
			_w = nodeMap.width;
			_h = nodeMap.height;
			_colors = new Color[_w * _h];
			//Clear the texture
			for(int y = 0; y < nodeMap.height; y++)
			{
				for(int x = 0; x < nodeMap.width; x++)
				{
					_colors[_w * y + x] = Color.clear;
				}
			}
			nodeMap.wrapMode = TextureWrapMode.Clamp;
			nodeMap.filterMode = FilterMode.Point;
			//Draw all nodes
			foreach (Node n in nodes)
			{
				DrawNode((int)n.position.x, (int)n.position.y, 6, n.color);
			}
			//Draw all connections
			foreach(Node n in nodes)
			{
				foreach (Node n2 in n.getConnections)
				{
					DrawConnection(n.position, n2.position);
				}
			}
			nodeMap.SetPixels(_colors);
			nodeMap.Apply();
			renderOutput.texture = nodeMap;
			Debug.Log("Finished rendering in " + System.DateTime.Now.Subtract(startTime).TotalMilliseconds + "ms");
		}

		void DrawConnection(Vector2 pos1, Vector2 pos2)
		{
			pos1 *= nodeMapResolution;
			//pos1.y = mapHeight * nodeMapResolution - pos1.y;
			pos2 *= nodeMapResolution;
			//pos2.y = mapHeight * nodeMapResolution - pos2.y;
			_colors = VoidUtils.DrawLine(_colors, _w, _h, (int)pos1.x, (int)pos1.y, (int)pos2.x, (int)pos2.y, 1, Color.red);
		}

		void DrawNode(int x, int y, int size, Color color)
		{
			x *= nodeMapResolution;
			y *= nodeMapResolution;
			//y = nodeMapResolution * mapHeight - y;
			_colors = VoidUtils.DrawCircle(_colors, _w, _h, x, y, size, color);
		}

		void GenerateNodeMap()
		{
			int generated = GenerateNodes();
			ConnectNodes();
			CleanUpExtraNodes(generated);
		}

		

		int GenerateNodes()
		{
			int cycles = 0;
			int nodesGenerated = 0;
			System.DateTime startTime;
			nodes = new List<Node>(nodesToGenerate);
			//Generate Nodes
			Debug.Log("Generating Nodes");
			startTime = System.DateTime.Now;
			for (int i = 0; i < nodesToGenerate; i++)
			{
				if (cycles >= maxGenerationCycles)
					break;
				bool validNode = true;
				Node node;
				//Create a node
				if (Random.Range(0, 4) == 1)
					node = new Town(new Vector2(Random.Range(0, mapWidth), Random.Range(0, mapHeight)));
				else
					node = new Village(new Vector2(Random.Range(0, mapWidth), Random.Range(0, mapHeight)));
				//Constrain node to the mapTexture
				int x, y;
				TransformToMapTexPos(node.position, out x, out y);
				if(heightMapTexture.GetPixel(x,y) == Color.clear)
				{
					i--; //Go back a step and skip next tests
					cycles++;
					continue;
				}
				//Make sure the new node isn't too close to another ndoe
				if (i != 0)
				{
					foreach (Node n in nodes)
					{
						if (Vector2.Distance(n.position, node.position) < minNodeDistance)
						{
							validNode = false;
							break;
						}
					}
				}
				if (validNode)
				{
					nodes.Add(node.Init(maxNodeConnections, maxConnectionDistance));
					nodesGenerated++;
				}
				else
					i--; //Go back a step incase of invalid node
				cycles++;
			}
			//Determine the outcome of node generation
			if (cycles >= maxGenerationCycles)
			{
				Debug.LogWarning("Failed to generate nodes in required cycles. " + nodesGenerated + " nodes generated.");
				return nodesGenerated;
			}
			else
				Debug.Log("Generated " + nodesGenerated + " nodes in " + System.DateTime.Now.Subtract(startTime).TotalMilliseconds + "ms with " + cycles + " cycles.");
			return nodesGenerated;
		}

		//Transform node coordinate to maptexture coordinate (lossy)
		void TransformToMapTexPos(Vector2 pos, out int x, out int y)
		{
			pos.x /= mapWidth;
			pos.y /= mapHeight;
			x = (int)(pos.x * heightMapTexture.width);
			y = (int)(pos.y * heightMapTexture.height);
		}

		void ConnectNodes()
		{
			//Connect Nodes
			Debug.Log("Connecting Nodes");
			System.DateTime startTime = System.DateTime.Now;
			int passes = 0;
			int failedConnectionAttempts = 0;
			int nodesConnected = 0;
			foreach (Node n in nodes)
			{
				//Stop connectiing if there are no nodes with 0 connections
				if (GetLowestNodeConnections() >= minNodeConnections)
					break;
				//Stop connecting if too many attempts were made
				if (failedConnectionAttempts >= connectionAttemptTimeOut)
					break;
				//Stop if the current node already has the maximum number of connections
				if (n.connectionCount >= n.maxConnections)
					continue;
				//Find the closest nodes to the current node
				List<Node> cNodes = GetClosestNodes(n);
				foreach (Node cN in cNodes)
				{

					//Stop connecting if too many attempts were made
					if (failedConnectionAttempts >= connectionAttemptTimeOut)
						break;
					//Check if the candidate nodes are within range
					float d = Vector2.Distance(n.position, cN.position);
					if (d > n.connectionRange && d > cN.connectionRange)
						continue;
					//Stop connecting once current node has reached max connections
					if (n.connectionCount == n.maxConnections)
						break;
					//Skip this candidate node if it already has max connections
					if (cN.connectionCount == cN.maxConnections)
					{
						failedConnectionAttempts++;
						continue;
					}
					else //Connect the nodes and reset the attempts counter
					{
						n.AddConnection(cN);
						failedConnectionAttempts = 0;
					}
					passes++;
				}

				if (failedConnectionAttempts == 0)
					nodesConnected++;
				failedConnectionAttempts = 0;

			}
			//Determine the results of the node connection process
			if (failedConnectionAttempts >= connectionAttemptTimeOut)
				Debug.LogError("Failed to connect nodes in required passes. " + nodesConnected + " nodes connected.");
			else
				Debug.Log("Connected all nodes in " + System.DateTime.Now.Subtract(startTime).TotalMilliseconds + "ms with " + passes + " passes.");
		}

		//Find the closest nodes to a given node
		List<Node> GetClosestNodes(Node node)
		{
			List<Node> cNodes = new List<Node>();
			float d = float.PositiveInfinity;
			foreach(Node n in nodes)
			{
				if (n.isConnected(node))
					continue;
				if (n == node)
					continue;
				float cd = Vector2.Distance(node.position, n.position);
				if(cd < d)
				{
					if (cNodes.Count >= node.maxConnections)
						cNodes.RemoveAt(0);
					d = cd;
					cNodes.Add(n);
				}
				
			}
			return cNodes;
		}

		//Get the count of the node with the lowest number of connections
		int GetLowestNodeConnections()
		{
			int min = 0;
			foreach(Node n in nodes)
			{
				min = (n.connectionCount < min) ? n.connectionCount : min;
				//if (min == 0)
				//	break;
			}
			return min;
		}

		//Discard nodes that were not connected to another node
		void CleanUpExtraNodes(int generated)
		{
			int discarded = 0;
			List<Node> remvoeList = new List<Node>();
			foreach (Node n in nodes)
			{
				if (n.connectionCount == 0)
				{
					discarded++;
					remvoeList.Add(n);
				}
			}
			foreach (Node n in remvoeList)
			{
				nodes.Remove(n);
			}
			remvoeList.Clear();
			Debug.Log(discarded + " of " + generated + " nodes discarded.");
		}
	}
}
