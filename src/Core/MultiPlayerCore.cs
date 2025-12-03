using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using WKMultiMod.src.Core;

public class MultiplayerCore : MonoBehaviour {

	// 服务器和客户端监听器 - 处理网络事件
	EventBasedNetListener serverListener;
	EventBasedNetListener clientListener;
	// 服务器和客户端管理器 - 管理网络连接
	public NetManager client;
	NetManager server;
	NetPeer serverPeer;         // 连接到服务器的对等端引用    

	// 最大玩家数量
	int maxPlayerCount;
	// 玩家字典 - 存储所有玩家对象, 键为玩家ID, 值为GameObject
	Dictionary<int, GameObject> players = new Dictionary<int, GameObject>();
	// 下一个玩家ID - 用于分配唯一的玩家标识符
	int nextPlayerId = 0;
	// 世界种子 - 用于同步游戏世界生成
	public int seed;

	// 数据包类型枚举 - 定义不同类型的网络消息
	enum PacketTypes {
		TransformUpdate = 0,    // 位置和旋转更新
		ConnectedToServer = 1,  // 连接成功通知
		SeedUpdate = 2,         // 世界种子更新
		CreatePlayer = 3,       // 创建新玩家
		RemovePlayer = 4,      // 移除玩家
	}

	// 注意：日志通过 MultiPalyerMain.Logger 访问

	void Awake() {
		MultiPalyerMain.Logger.LogInfo("MultiplayerCore 核心脚本启动 (DDOL 保护中).");

		// 初始化网络监听器和管理器
		serverListener = new EventBasedNetListener();
		server = new NetManager(serverListener);
		clientListener = new EventBasedNetListener();
		client = new NetManager(clientListener);

		// 订阅场景加载事件, 用于执行依赖于场景的操作（如命令注册）
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void Start() {
		MultiPalyerMain.Logger.LogInfo("MultiplayerCore 核心脚本 Start 方法调用.");
	}

	private void Update() {
		// 恢复网络事件轮询
		if (client != null) client.PollEvents();
		if (server != null && server.IsRunning) server.PollEvents();

		// 如果已连接到服务器, 持续更新位置
		// 注意：旧 Mod 使用 CL_Player, 这里使用 ENT_Player. 请确保 ENT_Player 是正确的类型. 
		if (serverPeer != null && ENT_Player.GetPlayer() != null) UpdateTransform();

		// 使用安全日志（修复了崩溃问题）
		if (Time.frameCount % 60000 == 0)
			MultiPalyerMain.Logger.LogInfo(
				$"Core Update 正在运行. Frame: " + Time.frameCount);
	}

	// 场景加载完成时调用
	private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
		MultiPalyerMain.Logger.LogInfo($"核心场景加载完成: {scene.name}");

		if (scene.name == "Game-Main") {
			// ... (命令注册和初始化代码保持不变) ...
			if (CommandConsole.instance != null) {
				InitWorldData();
				RegisterCommands();
			} else {
				MultiPalyerMain.Logger.LogError("场景加载后 CommandConsole 实例仍为 null, 无法注册命令.");
			}
		}
		if (scene.name == "Main-Menu") {
			//CloseLink();
		}
	}

	// 当核心对象被销毁时调用
	void OnDestroy() {
		// 核心对象被销毁时的清理工作
		MultiPalyerMain.Logger.LogError("MultiplayerCore 被销毁 (DDOL 失败).");
		SceneManager.sceneLoaded -= OnSceneLoaded;

		// 关闭网络连接
		CloseLink();
	}

	// 命令注册方法
	private void RegisterCommands() {
		// 将命令注册到 CommandConsole
		CommandConsole.AddCommand("host", Host, false);
		CommandConsole.AddCommand("join", Join, false);
		CommandConsole.AddCommand("leave", Leave, false);
		MultiPalyerMain.Logger.LogInfo("命令集 注册成功");
	}

	// 初始化世界数据的方法
	private void InitWorldData() {
		// 确保 WorldLoader.instance 存在时才获取种子
		if (WorldLoader.instance != null) {
			seed = WorldLoader.instance.seed;
			MultiPalyerMain.Logger.LogInfo("世界种子已获取:" + seed);
		} else {
			MultiPalyerMain.Logger.LogError("WorldLoader 实例不存在, 无法初始化世界种子. ");
		}
		maxPlayerCount = 4;
		//players.Clear();
	}

	// 更新本地玩家位置并发送到服务器
	private void UpdateTransform() {
		NetDataWriter writer = new NetDataWriter();
		writer.Put((int)PacketTypes.TransformUpdate);

		// 获取本地玩家位置和旋转
		Vector3 playerPosition = ENT_Player.GetPlayer().transform.position;
		Vector3 playerRotation = ENT_Player.GetPlayer().transform.eulerAngles;

		// 写入位置和旋转数据
		writer.Put(playerPosition.x);
		writer.Put(playerPosition.y);
		writer.Put(playerPosition.z);
		writer.Put(playerRotation.x);
		writer.Put(playerRotation.y);
		writer.Put(playerRotation.z);

		// 发送到服务器
		serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	// 关闭连接的方法
	private void CloseLink() {
		// 如果服务器正在运行，断开所有连接
		if (server != null && server.IsRunning) {
			server.DisconnectAll();
			server.Stop();
			MultiPalyerMain.Logger.LogInfo("服务器连接已停止.");
		}

		// 断开客户端连接
		if (client != null) {
			client.DisconnectAll();
			client.Stop();
			MultiPalyerMain.Logger.LogInfo("客户端连接已停止.");
		}

		serverPeer = null; // 重置对等端引用

		// 销毁所有玩家对象
		foreach (KeyValuePair<int, GameObject> player in players) {
			if (player.Value != null) {
				Destroy(player.Value);
			}
		}
		players.Clear();
	}

	// 命令实现
	public void Host(string[] args) {
		if (server == null) {
			MultiPalyerMain.Logger.LogError("服务器不存在");
			return;
		}

		// 修复：检查参数长度，防止 IndexOutOfRangeException
		if (args.Length < 1) {
			CommandConsole.LogError(
				"Usage: host <port> [max_players]\nExample: host 22222");
			return;
		}

		MultiPalyerMain.Logger.LogInfo("服务器存在");
		ushort port = ushort.Parse(args[0]);

		if (args.Length >= 2) {
			maxPlayerCount = int.Parse(args[1]);
		} else {
			maxPlayerCount = 4; // 默认值
		}

		server.Start(port);

		// 处理连接请求事件
		serverListener.ConnectionRequestEvent += request => {
			if (server.ConnectedPeersCount < maxPlayerCount) {
				request.Accept();
			} else {
				request.Reject();
			}
		};

		// 处理客户端连接成功事件
		serverListener.PeerConnectedEvent += peer => {
			peer.Tag = nextPlayerId;
			nextPlayerId++;

			MultiPalyerMain.Logger.LogInfo("新客户端已连接: " + peer.Tag.ToString());
			CommandConsole.Log("We got connection: " + peer.Tag.ToString());

			NetDataWriter writer = new NetDataWriter();

			// 1. 发送连接成功消息
			writer.Put((int)PacketTypes.ConnectedToServer);
			writer.Put(server.ConnectedPeersCount - 1);
			foreach (NetPeer connectedPeer in server.ConnectedPeerList) {
				if (connectedPeer.Tag == peer.Tag) continue;
				writer.Put((int)connectedPeer.Tag);
			}
			peer.Send(writer, DeliveryMethod.ReliableOrdered);

			// 2. 发送世界种子信息
			writer.Reset();
			writer.Put((int)PacketTypes.SeedUpdate);
			writer.Put(WorldLoader.instance.seed);
			peer.Send(writer, DeliveryMethod.ReliableOrdered);

			// 3. 通知所有客户端创建新玩家
			writer.Reset();
			writer.Put((int)PacketTypes.CreatePlayer);
			writer.Put((int)peer.Tag);
			server.SendToAll(writer, DeliveryMethod.ReliableOrdered, peer);
		};

		// 处理网络数据接收事件
		serverListener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) => {

			// 基本验证：确保数据足够读取一个整数（数据包类型）
			if (dataReader.AvailableBytes < 4) {
				dataReader.Recycle();
				return;
			}

			int packetType = dataReader.GetInt();

			switch (packetType) {
				case (int)PacketTypes.TransformUpdate:
					// 转发位置更新给所有其他客户端
					NetDataWriter writer = new NetDataWriter();
					writer.Put((int)PacketTypes.TransformUpdate);
					writer.Put((int)fromPeer.Tag);
					writer.Put(dataReader.GetRemainingBytes());
					server.SendToAll(writer, DeliveryMethod.ReliableOrdered, fromPeer);
					break;
			}
			dataReader.Recycle();
		};

		serverListener.PeerDisconnectedEvent += (peer, reason) => {

			// 1. 获取断开连接的玩家ID
			int disconnectedPlayerId = (int)peer.Tag;

			// 2. (主机端) 销毁本地的远程玩家代理对象
			// 注意：主机需要检查并移除它创建的远程玩家对象（即其他客户端的投影）。
			if (players.ContainsKey(disconnectedPlayerId)) {
				// 必须先销毁 GameObject，再从字典中移除
				Destroy(players[disconnectedPlayerId]);
				players.Remove(disconnectedPlayerId);
				MultiPalyerMain.Logger.LogInfo("主机已移除远程玩家代理: ID=" + disconnectedPlayerId);
			}

			// 3. 通知所有剩余的客户端移除该玩家
			NetDataWriter writer = new NetDataWriter();
			writer.Put((int)PacketTypes.RemovePlayer); // 假设 PacketTypes.RemovePlayer 是新添加的
			writer.Put(disconnectedPlayerId);

			MultiPalyerMain.Logger.LogInfo(
				"客户端断开连接: ID=" + disconnectedPlayerId + " Reason=" + reason);

			// 发送给所有剩余的连接（LiteNetLib 会自动排除已断开的 peer）
			server.SendToAll(writer, DeliveryMethod.ReliableOrdered);
		};

		// 主机作为客户端连接到自己的服务器
		Join(["127.0.0.1", port.ToString()]);

		MultiPalyerMain.Logger.LogInfo("已创建服务端");

		CommandConsole.Log("Hosting lobby...");
		CommandConsole.LogError(
			"You are a hosting a peer-to-peer lobby\n"
			+ "By sharing your IP you are also sharing your address\n"
			+ "Be careful... :)");
	}

	public void Join(string[] args) {
		if (client == null) {
			MultiPalyerMain.Logger.LogError("客户端不存在");
			return;
		}

		// 参数验证
		if (args.Length < 2) {
			CommandConsole.LogError(
				"Usage: join <IP> <port>\n"
				+ "Example: join 127.0.0.1 22222 or join [::1] 22222");
			return;
		}

		MultiPalyerMain.Logger.LogInfo("客户端存在");

		// 解析IP地址和端口
		string ip = args[0];
		int port = int.Parse(args[1]);

		// 启动客户端并连接到服务器
		client.Start();
		serverPeer = client.Connect(ip, port, "");

		// 处理客户端接收到的网络数据
		clientListener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) => {
			int packetType = dataReader.GetInt();

			switch (packetType) {
				case (int)PacketTypes.TransformUpdate:
					// 处理其他玩家的位置更新
					int playerId = dataReader.GetInt();
					Vector3 new_position = new Vector3(dataReader.GetFloat(), dataReader.GetFloat(), dataReader.GetFloat());
					Vector3 new_rotation = new Vector3(dataReader.GetFloat(), dataReader.GetFloat(), dataReader.GetFloat());

					if (!players.ContainsKey(playerId)) break;

					MultiplayerObject player = players[playerId].GetComponent<MultiplayerObject>();
					player.UpdatePosition(new_position);
					player.UpdateRotation(new_rotation);
					break;

				case (int)PacketTypes.ConnectedToServer:
					int peerCount = dataReader.GetInt();
					MultiPalyerMain.Logger.LogInfo("已连接 正在加载 " + peerCount.ToString() + " 玩家");
					CommandConsole.Log("Connected!\nCreating " + peerCount.ToString() + " player instance(s).");
					for (int i = 0; i < peerCount; i++) {
						CreatePlayer(dataReader.GetInt());
					}
					break;

				case (int)PacketTypes.SeedUpdate:
					seed = dataReader.GetInt();
					WorldLoader.ReloadWithSeed([seed.ToString()]);
					break;

				case (int)PacketTypes.CreatePlayer:
					int Tag = dataReader.GetInt();
					CreatePlayer(Tag);
					break;

				case (int)PacketTypes.RemovePlayer:
					int playerIdToRemove = dataReader.GetInt();

					if (players.ContainsKey(playerIdToRemove)) {
						// 销毁远程玩家对象
						Destroy(players[playerIdToRemove]);
						players.Remove(playerIdToRemove);
						MultiPalyerMain.Logger.LogInfo($"客户端已移除远程玩家: ID={playerIdToRemove}");
					}
					break;
			}
			dataReader.Recycle();
		};
		MultiPalyerMain.Logger.LogInfo("尝试连接: " + ip);
		CommandConsole.Log("Trying to join ip: " + ip + "...");

	}

	public void Leave(string[] args) {
		CloseLink();
		MultiPalyerMain.Logger.LogInfo("测试Leave成功: 所有连接已断开, 远程玩家已清理. ");
	}

	// 创建玩家视觉表现
	private void CreatePlayer(int Tag) {
		MultiPalyerMain.Logger.LogInfo("创建玩家中" + Tag);

		// 创建玩家游戏对象
		GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
		player.name = "RemotePlayer_" + Tag;
		MultiPalyerMain.Logger.LogInfo("创建玩家对象");

		// 添加 ObjectTagger 组件
		ObjectTagger tagger = player.AddComponent<ObjectTagger>();
		if (tagger != null) {
			tagger.tags.Add("Handhold");
		}
		MultiPalyerMain.Logger.LogInfo("添加 ObjectTagger 组件");

		// 添加 CL_Handhold 组件 (攀爬逻辑)
		CL_Handhold handholdComponent = player.AddComponent<CL_Handhold>();
		if (handholdComponent != null) {
			//// 禁用组件，阻止其 Start() 和 Update() 运行
			//handholdComponent.enabled = false;

			// 手动替代 Start() 的关键步骤: 确保事件不为 null
			handholdComponent.stopEvent = new UnityEvent();
			handholdComponent.activeEvent = new UnityEvent();
			//handholdComponent.hammerEvent = new UnityEvent(); // 确保所有事件都初始化
		}
		MultiPalyerMain.Logger.LogInfo("添加 CL_Handhold 组件");

		// 确保 Renderer 被赋值，否则 Material 设置会崩溃
		Renderer objectRenderer = player.GetComponent<Renderer>();
		if (objectRenderer != null) {
			player.GetComponent<CL_Handhold>().handholdRenderer = objectRenderer;
		}
		MultiPalyerMain.Logger.LogInfo("分配 Renderer 给 CL_Handhold 组件");

		// 设置碰撞体为触发器 (Collider/Trigger)
		CapsuleCollider collider = player.GetComponent<CapsuleCollider>();
		if (collider != null) {
			collider.isTrigger = true;
			// 调整尺寸 (如果需要，但默认尺寸通常可以直接使用)
			collider.radius = 0.5f;
			collider.height = 2.0f;
		}
		MultiPalyerMain.Logger.LogInfo("添加并配置 CapsuleCollider 组件");

		// 添加 玩家 组件以处理位置和旋转更新
		player.AddComponent<MultiplayerObject>();
		MultiPalyerMain.Logger.LogInfo("添加 MultiplayerObject 组件");

		// 设置材质 (Renderer/Graphics)
		Material bodyMaterial = new Material(Shader.Find("Unlit/Color"));
		bodyMaterial.color = Color.gray;
		player.GetComponent<Renderer>().material = bodyMaterial;
		MultiPalyerMain.Logger.LogInfo("设置玩家材质");

		// 将玩家添加到字典中
		players.Add(Tag, player);

		// 设置为不销毁
		DontDestroyOnLoad(player);

		// 输出创建成功信息
		MultiPalyerMain.Logger.LogInfo("创建玩家成功 ID:" + Tag);
		CommandConsole.Log("Creating Player with Tag: " + player.GetInstanceID());
	}
}

public class MultiplayerObject : MonoBehaviour {
	int id;  // 玩家ID, 用于在网络中识别不同的玩家实例

	// 更新玩家位置的方法
	public void UpdatePosition(Vector3 new_position) {
		// 在控制台输出新位置坐标, 用于调试
		if (Time.frameCount % 600 == 0)
			MultiPalyerMain.Logger.LogInfo(new_position.ToString());

		// 实际更新游戏对象的位置
		transform.position = new_position;
	}

	// 更新玩家旋转的方法
	public void UpdateRotation(Vector3 new_rotation) {
		// 设置游戏对象的欧拉角旋转
		transform.eulerAngles = new_rotation;
	}
}

