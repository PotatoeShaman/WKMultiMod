using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Generic;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using WKMultiMod.Main;

namespace WKMultiMod.Core;

public class MultiPlayerCore : MonoBehaviour {

	// 服务器和客户端监听器 - 处理网络事件
	private EventBasedNetListener _serverListener;
	private EventBasedNetListener _clientListener;
	// 服务器和客户端管理器 - 管理网络连接
	private NetManager _client;
	private NetManager _server;
	// 连接到服务器的对等端引用    
	private NetPeer _serverPeer;

	// 最大玩家数量
	private int _maxPlayerCount;
	// 玩家字典 - 存储所有玩家对象, 键为玩家ID, 值为GameObject
	private Dictionary<int, GameObject> _remotePlayers = new Dictionary<int, GameObject>();
	// 下一个玩家ID - 用于分配唯一的玩家标识符
	private int _nextPlayerId = 0;
	// 世界种子 - 用于同步游戏世界生成
	public int WorldSeed { get; private set; }

	// 数据包类型枚举 - 定义不同类型的网络消息
	enum PacketType {
		TransformUpdate = 0,    // 位置和旋转更新
		ConnectedToServer = 1,  // 连接成功通知
		SeedUpdate = 2,         // 世界种子更新
		CreatePlayer = 3,       // 创建新玩家
		RemovePlayer = 4,      // 移除玩家
	}

	// 注意：日志通过 MultiPalyerMain.Logger 访问

	void Awake() {
		MultiPlayerMain.Logger.LogInfo("[MP Mod loading] MultiplayerCore Awake");

		// 初始化网络监听器和管理器
		_serverListener = new EventBasedNetListener();
		_server = new NetManager(_serverListener);
		_clientListener = new EventBasedNetListener();
		_client = new NetManager(_clientListener);

		// 订阅场景加载事件, 用于执行依赖于场景的操作（如命令注册）
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void Start() {
		MultiPlayerMain.Logger.LogInfo("[MP Mod loading] MultiplayerCore Start");
	}

	private void Update() {
		// 恢复网络事件轮询
		if (_client != null) _client.PollEvents();
		if (_server != null && _server.IsRunning) _server.PollEvents();

		// 如果已连接到服务器, 持续更新位置. 
		if (_serverPeer != null && ENT_Player.GetPlayer() != null)
			UpdatePlayerTransform();
	}

	// 更新本地玩家位置并发送到服务器
	private void UpdatePlayerTransform() {
		NetDataWriter writer = new NetDataWriter();
		writer.Put((int)PacketType.TransformUpdate);

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
		_serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	// 场景加载完成时调用
	private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
		MultiPlayerMain.Logger.LogInfo("[MP Mod] 核心场景加载完成: " + scene.name);

		if (scene.name == "Game-Main") {
			// 注册命令和初始化世界数据
			if (CommandConsole.instance != null) {
				InitializeData();
				RegisterCommands();
			} else {
				MultiPlayerMain.Logger.LogError("[MP Mod] 场景加载后 CommandConsole 实例仍为 null, 无法注册命令.");
			}
		}
		if (scene.name == "Main-Menu") {
			// 返回主菜单时关闭连接
			CloseAllConnections();
		}
	}

	// 当核心对象被销毁时调用
	void OnDestroy() {
		// 核心对象被销毁时的清理工作
		MultiPlayerMain.Logger.LogError("[MP Mod loading] MultiplayerCore 被销毁");
		SceneManager.sceneLoaded -= OnSceneLoaded;

		// 关闭网络连接
		CloseAllConnections();
	}

	// 命令注册
	private void RegisterCommands() {
		// 将命令注册到 CommandConsole
		CommandConsole.AddCommand("host", Host);
		CommandConsole.AddCommand("join", Join);
		CommandConsole.AddCommand("leave", Leave);
		MultiPlayerMain.Logger.LogInfo("[MP Mod loading] 命令集 注册成功");
	}

	// 初始化数据
	private void InitializeData() {
		_maxPlayerCount = 4;
		//players.Clear();
	}

	// 关闭所有连接
	private void CloseAllConnections() {
		// 如果服务器正在运行, 断开所有连接
		if (_server != null) {
			// 取消订阅服务器事件
			_serverListener.ConnectionRequestEvent -= HandleConnectionRequest;
			_serverListener.PeerConnectedEvent -= HandlePeerConnected;
			_serverListener.NetworkReceiveEvent -= HandleNetworkReceive;
			_serverListener.PeerDisconnectedEvent -= HandlePeerDisconnected;

			// 断开所有客户端连接
			_server.DisconnectAll();

			// 停止服务器
			if (_server.IsRunning) {
				_server.Stop();
			}

			MultiPlayerMain.Logger.LogInfo("[MP Mod Close] 服务器连接已停止.");
		}

		// 断开客户端连接
		if (_client != null) {
			// 取消订阅客户端事件
			_clientListener.NetworkReceiveEvent -= HandleClientNetworkReceive;

			// 断开与服务器的连接
			_client.DisconnectAll();

			// 停止客户端
			if (_client.IsRunning) {
				_client.Stop();
			}

			MultiPlayerMain.Logger.LogInfo("[MP Mod Close] 客户端连接已停止.");
		}

		_serverPeer = null; // 重置对等端引用

		// 销毁所有玩家对象
		foreach (KeyValuePair<int, GameObject> player in _remotePlayers) {
			if (player.Value != null) {
				Destroy(player.Value);
			}
		}
		_remotePlayers.Clear();

		// 重置多人游戏活动标志
		MultiPlayerMain.IsMultiplayerActive = false;
	}

	// 服务器端：处理连接请求
	private void HandleConnectionRequest(ConnectionRequest request) {
		if (_server.ConnectedPeersCount < _maxPlayerCount) {
			request.Accept();
		} else {
			request.Reject();
		}
	}

	// 服务器端：处理新客户端连接
	private void HandlePeerConnected(NetPeer peer) {
		peer.Tag = _nextPlayerId;
		_nextPlayerId++;

		MultiPlayerMain.Logger.LogInfo("[MP Mod server] 新客户端已连接: ID= " + peer.Tag.ToString());
		//CommandConsole.Log("We got connection: " + peer.Tag);
		CommandConsole.Log("We got new connection");

		// 发送连接成功消息
		SendConnectionSuccessMessage(peer);

		// 发送世界种子信息
		SendWorldSeedToPeer(peer);

		// 通知所有客户端创建新玩家
		NotifyAllClientsToCreatePlayer(peer);
	}

	// 服务器端：发送连接成功消息给新客户端
	private void SendConnectionSuccessMessage(NetPeer peer) {
		NetDataWriter writer = new NetDataWriter();
		writer.Put((int)PacketType.ConnectedToServer);
		writer.Put(_server.ConnectedPeersCount - 1);

		foreach (NetPeer connectedPeer in _server.ConnectedPeerList) {
			if ((int)connectedPeer.Tag == (int)peer.Tag) continue;
			writer.Put((int)connectedPeer.Tag);
		}

		peer.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	// 服务器端：发送世界种子给指定客户端
	private void SendWorldSeedToPeer(NetPeer peer) {
		NetDataWriter writer = new NetDataWriter();
		writer.Put((int)PacketType.SeedUpdate);
		writer.Put(WorldLoader.instance.seed);
		peer.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	// 服务器端：通知所有客户端创建新玩家
	private void NotifyAllClientsToCreatePlayer(NetPeer peer) {
		NetDataWriter writer = new NetDataWriter();
		writer.Put((int)PacketType.CreatePlayer);
		writer.Put((int)peer.Tag);
		_server.SendToAll(writer, DeliveryMethod.ReliableOrdered, peer);
	}

	// 服务器端：处理网络数据接收
	private void HandleNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
		// 基本验证：确保数据足够读取一个整数（数据包类型）
		if (reader.AvailableBytes < 4) {
			reader.Recycle();
			return;
		}

		int packetType = reader.GetInt();

		switch (packetType) {
			case (int)PacketType.TransformUpdate:
				ForwardTransformUpdate(peer, reader);
				break;
		}

		reader.Recycle();
	}

	// 服务器端：转发位置更新给所有其他客户端
	private void ForwardTransformUpdate(NetPeer peer, NetPacketReader reader) {
		NetDataWriter writer = new NetDataWriter();
		writer.Put((int)PacketType.TransformUpdate);
		writer.Put((int)peer.Tag);
		writer.Put(reader.GetRemainingBytes());
		_server.SendToAll(writer, DeliveryMethod.ReliableOrdered, peer);
	}

	// 服务器端：处理客户端断开连接
	private void HandlePeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
		int disconnectedPlayerId = (int)peer.Tag;

		// 主机端：销毁本地的远程玩家代理对象
		if (_remotePlayers.ContainsKey(disconnectedPlayerId)) {
			Destroy(_remotePlayers[disconnectedPlayerId]);
			_remotePlayers.Remove(disconnectedPlayerId);
			MultiPlayerMain.Logger.LogInfo("[MP Mod server] 主机已移除远程玩家 ID: " + disconnectedPlayerId);
		}

		// 通知所有剩余的客户端移除该玩家
		NotifyAllClientsToRemovePlayer(disconnectedPlayerId);
	}

	// 服务器端：通知所有客户端移除玩家
	private void NotifyAllClientsToRemovePlayer(int playerId) {
		NetDataWriter writer = new NetDataWriter();
		writer.Put((int)PacketType.RemovePlayer);
		writer.Put(playerId);
		_server.SendToAll(writer, DeliveryMethod.ReliableOrdered);
	}

	// 客户端：处理接收到的网络数据
	private void HandleClientNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
		// 基本验证：确保数据足够读取一个整数（数据包类型）
		if (reader.AvailableBytes < 4) {
			reader.Recycle();
			return;
		}

		int packetType = reader.GetInt();

		switch (packetType) {
			case (int)PacketType.TransformUpdate:
				HandleTransformUpdate(reader);
				break;

			case (int)PacketType.ConnectedToServer:
				HandleConnectionSuccess(reader);
				break;

			case (int)PacketType.SeedUpdate:
				HandleSeedUpdate(reader);
				break;

			case (int)PacketType.CreatePlayer:
				HandleCreatePlayer(reader);
				break;

			case (int)PacketType.RemovePlayer:
				HandleRemovePlayer(reader);
				break;
		}

		reader.Recycle();
	}

	// 客户端：处理其他玩家的位置更新
	private void HandleTransformUpdate(NetPacketReader reader) {
		int playerId = reader.GetInt();
		Vector3 newPosition = new Vector3(
			reader.GetFloat(),
			reader.GetFloat(),
			reader.GetFloat()
		);
		Vector3 newRotation = new Vector3(
			reader.GetFloat(),
			reader.GetFloat(),
			reader.GetFloat()
		);

		if (!_remotePlayers.ContainsKey(playerId)) return;

		MultiPlayerObject player = _remotePlayers[playerId].GetComponent<MultiPlayerObject>();
		player.UpdatePosition(newPosition);
		player.UpdateRotation(newRotation);
	}

	// 客户端：处理连接成功消息
	private void HandleConnectionSuccess(NetPacketReader reader) {
		int peerCount = reader.GetInt();
		MultiPlayerMain.Logger.LogInfo("[MP Mod client] 已连接, 正在加载 " + peerCount.ToString() + " 玩家");
		CommandConsole.Log(
			"Connected!\nCreating "
			+ peerCount
			+ " player instance(s).");

		for (int i = 0; i < peerCount; i++) {
			CreateRemotePlayer(reader.GetInt());
		}
	}

	// 客户端：处理加载世界种子
	private void HandleSeedUpdate(NetPacketReader reader) {
		WorldSeed = reader.GetInt();
		MultiPlayerMain.Logger.LogInfo("[MP Mod client] 加载世界, 种子号: " + WorldSeed.ToString());
		WorldLoader.ReloadWithSeed(new string[] { WorldSeed.ToString() });
	}

	// 客户端：处理创建玩家消息
	private void HandleCreatePlayer(NetPacketReader reader) {
		int playerId = reader.GetInt();
		CreateRemotePlayer(playerId);
	}

	// 客户端：处理移除玩家消息
	private void HandleRemovePlayer(NetPacketReader reader) {
		int playerIdToRemove = reader.GetInt();

		if (_remotePlayers.ContainsKey(playerIdToRemove)) {
			Destroy(_remotePlayers[playerIdToRemove]);
			_remotePlayers.Remove(playerIdToRemove);
			MultiPlayerMain.Logger.LogInfo("[MP Mod client] 客户端已移除远程玩家: ID=" + playerIdToRemove);
		}
	}


	// 命令实现
	public void Host(string[] args) {
		// 先关闭现有连接
		CloseAllConnections();

		if (_server == null) {
			MultiPlayerMain.Logger.LogError("[MP Mod server] 服务器管理器不存在");
			return;
		}

		// 修复：检查参数长度, 防止 IndexOutOfRangeException
		if (args.Length < 1) {
			CommandConsole.LogError(
				"Usage: host <port> [max_players]\nExample: host 22222");
			return;
		}

		ushort port = ushort.Parse(args[0]);

		if (args.Length >= 2) {
			_maxPlayerCount = int.Parse(args[1]);
		} else {
			_maxPlayerCount = 4; // 默认值
		}

		_server.Start(port); // 在指定端口启动服务器

		// 订阅服务器事件
		_serverListener.ConnectionRequestEvent += HandleConnectionRequest;
		_serverListener.PeerConnectedEvent += HandlePeerConnected;
		_serverListener.NetworkReceiveEvent += HandleNetworkReceive;
		_serverListener.PeerDisconnectedEvent += HandlePeerDisconnected;

		// 主机作为客户端连接到自己的服务器
		Join(["127.0.0.1", port.ToString()]);

		MultiPlayerMain.Logger.LogInfo("[MP Mod server] 已创建服务端");

		CommandConsole.Log("Hosting lobby...");
		CommandConsole.LogError(
			"You are a hosting a peer-to-peer lobby\n"
			+ "By sharing your IP you are also sharing your address\n"
			+ "Be careful... :)");
	}

	public void Join(string[] args) {
		// 先取消可能存在的客户端订阅
		_clientListener.NetworkReceiveEvent -= HandleClientNetworkReceive;

		if (_client == null) {
			MultiPlayerMain.Logger.LogError("[MP Mod client] 客户端管理器不存在");
			return;
		}

		// 参数验证
		if (args.Length < 2) {
			CommandConsole.LogError(
				"Usage: join <IP> <port>\n"
				+ "Example: join 127.0.0.1 22222 or join [::1] 22222");
			return;
		}

		// 解析IP地址和端口
		string ip = args[0];
		int port = int.Parse(args[1]);

		// 如果客户端已经在运行, 先停止
		if (_client.IsRunning) {
			_client.DisconnectAll();
			_client.Stop();
		}

		// 启动客户端并连接到服务器
		_client.Start();
		_serverPeer = _client.Connect(ip, port, "");

		// 设置多人游戏活动标志
		MultiPlayerMain.IsMultiplayerActive = true;

		// 处理客户端接收到的网络数据
		_clientListener.NetworkReceiveEvent += HandleClientNetworkReceive;

		MultiPlayerMain.Logger.LogInfo("[MP Mod server] 尝试连接: " + ip);
		CommandConsole.Log("Trying to join ip: " + ip);
	}

	public void Leave(string[] args) {
		CloseAllConnections();
		MultiPlayerMain.Logger.LogInfo("[MP Mod] 所有连接已断开, 远程玩家已清理.");
	}

	// 创建玩家视觉表现
	private void CreateRemotePlayer(int Tag) {
		MultiPlayerMain.Logger.LogInfo("[MP Mod create] 创建玩家中" + Tag);

		// 创建玩家游戏对象
		GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
		player.name = "RemotePlayer_" + Tag;
		//MultiPalyerMain.Logger.LogInfo("[MP Mod create] 创建玩家对象");

		// 添加 ObjectTagger 组件
		ObjectTagger tagger = player.AddComponent<ObjectTagger>();
		if (tagger != null) {
			tagger.tags.Add("Handhold");
		}
		//MultiPalyerMain.Logger.LogInfo("[MP Mod create] 添加 ObjectTagger 组件");

		// 添加 CL_Handhold 组件 (攀爬逻辑)
		CL_Handhold handholdComponent = player.AddComponent<CL_Handhold>();
		if (handholdComponent != null) {
			// 添加停止和激活事件
			handholdComponent.stopEvent = new UnityEvent();
			handholdComponent.activeEvent = new UnityEvent();
		}
		//MultiPalyerMain.Logger.LogInfo("[MP Mod create] 添加 CL_Handhold 组件");

		// 确保 Renderer 被赋值, 否则 Material 设置会崩溃
		Renderer objectRenderer = player.GetComponent<Renderer>();
		if (objectRenderer != null) {
			player.GetComponent<CL_Handhold>().handholdRenderer = objectRenderer;
		}
		//MultiPalyerMain.Logger.LogInfo("[MP Mod create] 分配 Renderer 给 CL_Handhold 组件");

		// 设置碰撞体为触发器 (Collider/Trigger)
		CapsuleCollider collider = player.GetComponent<CapsuleCollider>();
		if (collider != null) {
			collider.isTrigger = true;
			// 调整尺寸 (如果需要, 但默认尺寸通常可以直接使用)
			collider.radius = 0.5f;
			collider.height = 2.0f;
		}
		//MultiPalyerMain.Logger.LogInfo("[MP Mod create] 添加并配置 CapsuleCollider 组件");

		// 添加 玩家 组件以处理位置和旋转更新
		player.AddComponent<MultiPlayerObject>();
		//MultiPalyerMain.Logger.LogInfo("[MP Mod create] 添加 MultiplayerObject 组件");

		// 设置材质 (Renderer/Graphics)
		Material bodyMaterial = new Material(Shader.Find("Unlit/Color"));
		bodyMaterial.color = Color.gray;
		player.GetComponent<Renderer>().material = bodyMaterial;
		//MultiPalyerMain.Logger.LogInfo("[MP Mod create] 设置玩家材质");

		// 将玩家添加到字典中
		_remotePlayers.Add(Tag, player);

		// 设置为不销毁
		DontDestroyOnLoad(player);

		// 输出创建成功信息
		MultiPlayerMain.Logger.LogInfo("[MP Mod create] 创建玩家成功 ID:" + Tag);
		CommandConsole.Log("Creating Player with Tag: " + player.GetInstanceID());
	}
}

public class MultiPlayerObject : MonoBehaviour {
	int id;  // 玩家ID, 用于在网络中识别不同的玩家实例

	// 更新玩家位置的方法
	public void UpdatePosition(Vector3 new_position) {
		// 实际更新游戏对象的位置
		transform.position = new_position;
	}

	// 更新玩家旋转的方法
	public void UpdateRotation(Vector3 new_rotation) {
		// 设置游戏对象的欧拉角旋转
		transform.eulerAngles = new_rotation;
	}
}

