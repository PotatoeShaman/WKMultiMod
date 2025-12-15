//using global::WKMultiMod.src.Data;
//using LiteNetLib;
//using LiteNetLib.Utils;
//using MonoMod.Core.Utils;
//using Steamworks.Data;
//using System;
//using System.Collections.Generic;
//using UnityEngine;
//using UnityEngine.Events;
//using UnityEngine.SceneManagement;
//using WKMultiMod.src.Util;
//using Quaternion = UnityEngine.Quaternion;
//using Vector3 = UnityEngine.Vector3;

//namespace WKMultiMod.src.Core;

//public class MPLiteNet : MonoBehaviour {

//	// 单例实例
//	public static MPLiteNet Instance { get; private set; }
//	// 标识这是否是"有效"实例(防止使用游戏初期被销毁的实例)
//	public static bool HasValidInstance => Instance != null && Instance.isActiveAndEnabled;

//	// 服务器和客户端监听器 - 处理网络事件
//	private EventBasedNetListener _serverListener;
//	private EventBasedNetListener _clientListener;
//	// 服务器和客户端管理器 - 管理网络连接
//	private NetManager _client;
//	private NetManager _server;
//	// 连接到服务器的对等端引用    
//	private NetPeer _serverPeer;
//	// 网络初始化状态
//	public bool IsInitialized { get; private set; }



//	// 最大玩家数量
//	private int _maxPlayerCount;
//	// 下一个可用玩家ID
//	private int _nextPlayerId = 0;
//	// 单独的方法来获取并递增
//	public int GetNextPlayerId() => _nextPlayerId++;

//	// 玩家字典 - 存储所有玩家对象, 键为玩家ID, 值为GameObject
//	private Dictionary<long, GameObject> _remotePlayerObjects = new Dictionary<long, GameObject>();
//	// 手部字典 - 存储所有玩家手部对象, 键为玩家ID, 值为GameObject
//	private Dictionary<long, GameObject> _remoteLeftHandObjects = new Dictionary<long, GameObject>();
//	// 手部字典 - 存储所有玩家手部对象, 键为玩家ID, 值为GameObject
//	private Dictionary<long, GameObject> _remoteRightHandObjects = new Dictionary<long, GameObject>();

//	// 世界种子 - 用于同步游戏世界生成
//	public int WorldSeed { get; private set; }
//	// 用于控制是否启用关卡标准化 Patch
//	public static bool IsMultiplayerActive { get; private set; } = false;
//	// 混乱模式开关
//	public static bool IsChaosMod { get; private set; } = false;

//	// 注意：日志通过 MultiPlayerMain.Logger 访问

//	void Awake() {
//		MPMain.Logger.LogInfo("[MP Mod loading] MultiplayerCore Awake");

//		Instance = this;

//		// 但如果是重复的活跃实例,销毁新的
//		if (FindObjectsOfType<MultiPlayerCore>().Length > 1) {
//			// 检查哪个实例是SteamManager的子对象(应该是持久的)
//			var allCores = FindObjectsOfType<MultiPlayerCore>();
//			MultiPlayerCore steamChildCore = null;
//			MultiPlayerCore otherCore = null;

//			foreach (var core in allCores) {
//				if (core.transform.parent != null &&
//					core.transform.parent.name.Contains("SteamManager")) {
//					steamChildCore = core;
//				} else if (core != this) {
//					otherCore = core;
//				}
//			}

//			// 如果已经有一个作为SteamManager子对象的实例,销毁其他的
//			if (steamChildCore != null && steamChildCore != this) {
//				MPMain.Logger.LogWarning("[MP Mod loading] 检测到重复的核心实例,销毁非Steam子对象");
//				Destroy(gameObject);
//				return;
//			}
//		}

//		// 初始化网络监听器和管理器
//		InitializeNetwork();
//	}

//	void Start() {
//		// 延迟验证,确保这是持久实例
//		if (transform.parent == null) {
//			MPMain.Logger.LogWarning("[MP Mod loading] 核心实例没有父对象,可能被游戏销毁");
//		} else {
//			MPMain.Logger.LogInfo("[MP Mod loading] 核心实例已附加到: " + transform.parent.name);
//		}
//	}

//	private void Update() {
//		// 恢复网络事件轮询
//		if (_client != null) _client.PollEvents();
//		if (_server != null && _server.IsRunning) _server.PollEvents();

//		// 如果已连接到服务器, 持续更新位置. 
//		if (_serverPeer != null && ENT_Player.GetPlayer() != null) {
//			SendPlayerTransform();
//			SendHandTransform();
//		}
//	}

//	// 当核心对象被销毁时调用
//	private void OnDestroy() {
//		// 只有当前实例是单例时才清理
//		if (Instance == this) {
//			Instance = null;
//		}

//		// 取消订阅场景加载事件
//		SceneManager.sceneLoaded -= OnSceneLoaded;
//		// 关闭网络连接和重置状态
//		CloseAllConnections();
//		ResetStateVariables();

//		MPMain.Logger.LogInfo("[MP Mod destroy] MultiPlayerCore 已被销毁");
//	}

//	// 网络初始化
//	private void InitializeNetwork() {
//		try {
//			_serverListener = new EventBasedNetListener();
//			_server = new NetManager(_serverListener);
//			_clientListener = new EventBasedNetListener();
//			_client = new NetManager(_clientListener);

//			SceneManager.sceneLoaded += OnSceneLoaded;

//			IsInitialized = true;
//			MPMain.Logger.LogInfo("[MP Mod init] 网络系统初始化完成");
//		} catch (Exception e) {
//			MPMain.Logger.LogError("[MP Mod init] 网络初始化失败: " + e.Message);
//			IsInitialized = false;
//		}

//	}

//	// 初始化数据
//	private void InitializeData() {
//		_maxPlayerCount = 4;
//	}

//	// 客户端 发送 服务器: 本地玩家位置
//	private void SendPlayerTransform() {
//		NetDataWriter writer = new NetDataWriter();
//		writer.Put((int)PacketType.PlayerDataUpdate);

//		// 获取本地玩家位置和旋转
//		Vector3 playerPosition = ENT_Player.GetPlayer().transform.position;
//		Vector3 playerRotation = ENT_Player.GetPlayer().transform.eulerAngles;

//		// 写入位置和旋转数据
//		writer.Put(playerPosition.x);
//		writer.Put(playerPosition.y);
//		writer.Put(playerPosition.z);
//		writer.Put(playerRotation.x);
//		writer.Put(playerRotation.y);
//		writer.Put(playerRotation.z);

//		// 发送到服务器
//		_serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
//	}

//	// 客户端 发送 服务器: 本地玩家手部位置
//	private void SendHandTransform() {
//		NetDataWriter writer = new NetDataWriter();
//		writer.Put((int)PacketType.PlayerDataUpdate);
//		ENT_Player.Hand leftHand = ENT_Player.GetPlayer().hands[0];
//		ENT_Player.Hand rightHand = ENT_Player.GetPlayer().hands[1];
//		writer.Put(leftHand.IsFree());
//		writer.Put(rightHand.IsFree());
//		if (!leftHand.IsFree()) {
//			// 获取本地玩家手部位置
//			Vector3 LeftPosition = leftHand.GetHoldWorldPosition();
//			writer.Put(LeftPosition.x);
//			writer.Put(LeftPosition.y);
//			writer.Put(LeftPosition.z);
//		}
//		if (!rightHand.IsFree()) {
//			// 获取本地玩家手部位置
//			Vector3 RightPosition = rightHand.GetHoldWorldPosition();
//			writer.Put(RightPosition.x);
//			writer.Put(RightPosition.y);
//			writer.Put(RightPosition.z);
//		}

//		// 发送到服务器
//		_serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
//	}

//	// 场景加载完成时调用
//	private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
//		MPMain.Logger.LogInfo("[MP Mod] 核心场景加载完成: " + scene.name);

//		IsChaosMod = false;

//		if (scene.name == "Game-Main") {
//			// 注册命令和初始化世界数据
//			if (CommandConsole.instance != null) {
//				InitializeData();
//				RegisterCommands();
//			} else {
//				MPMain.Logger.LogError("[MP Mod] 场景加载后 CommandConsole 实例仍为 null, 无法注册命令.");
//			}
//		}
//		if (scene.name == "Main-Menu") {
//			// 返回主菜单时关闭连接 重设置
//			CloseAllConnections();
//			ResetStateVariables();
//		}
//	}

//	// 命令注册
//	private void RegisterCommands() {
//		// 将命令注册到 CommandConsole
//		CommandConsole.AddCommand("host", Host);
//		CommandConsole.AddCommand("join", Join);
//		CommandConsole.AddCommand("leave", Leave);
//		CommandConsole.AddCommand("chaos", ChaosMod);
//		MPMain.Logger.LogInfo("[MP Mod loading] 命令集 注册成功");
//	}

//	// 关闭所有连接
//	private void CloseAllConnections() {
//		// 如果服务器正在运行, 断开所有连接
//		if (_server != null) {
//			// 取消订阅服务器事件
//			_serverListener.ConnectionRequestEvent -= ProcessConnectionRequest;
//			_serverListener.PeerConnectedEvent -= ProcessPeerConnected;
//			_serverListener.NetworkReceiveEvent -= ProcessNetworkReceive;
//			_serverListener.PeerDisconnectedEvent -= HandlePeerDisconnected;

//			// 断开所有客户端连接
//			_server.DisconnectAll();

//			// 停止服务器
//			if (_server.IsRunning) {
//				_server.Stop();
//			}

//			MPMain.Logger.LogInfo("[MP Mod Close] 服务器连接已停止.");
//		}

//		// 断开客户端连接
//		if (_client != null) {
//			// 取消订阅客户端事件
//			_clientListener.NetworkReceiveEvent -= ProcessClientNetworkReceive;

//			// 断开与服务器的连接
//			_client.DisconnectAll();

//			// 停止客户端
//			if (_client.IsRunning) {
//				_client.Stop();
//			}

//			MPMain.Logger.LogInfo("[MP Mod Close] 客户端连接已停止.");
//		}

//		_serverPeer = null; // 重置对等端引用

//		// 销毁所有玩家对象
//		foreach (KeyValuePair<long, GameObject> player in _remotePlayerObjects) {
//			if (player.Value != null) {
//				Destroy(player.Value);
//			}
//		}

//		_remotePlayerObjects.Clear();
//		_remoteLeftHandObjects.Clear();
//		_remoteRightHandObjects.Clear();
//	}

//	// 重置设置
//	private void ResetStateVariables() {
//		IsMultiplayerActive = false;
//		IsChaosMod = false;
//	}

//	// 服务器端 处理 新客户端：连接请求
//	private void ProcessConnectionRequest(ConnectionRequest request) {
//		if (_server.ConnectedPeersCount < _maxPlayerCount) {
//			request.Accept();
//		} else {
//			request.Reject();
//		}
//	}

//	// 服务器端 处理 新客户端：新客户端连接
//	private void ProcessPeerConnected(NetPeer peer) {
//		peer.Tag = _nextPlayerId;
//		_nextPlayerId++;

//		MPMain.Logger.LogInfo("[MP Mod server] 新客户端已连接: ID= " + peer.Tag.ToString());
//		//CommandConsole.Log("We got connection: " + peer.Tag);
//		CommandConsole.Log("We got new connection");

//		// 发送连接成功消息
//		SendConnectionSuccessMessage(peer);

//		// 发送世界种子信息
//		SendWorldSeedToPeer(peer);

//		// 通知所有客户端创建新玩家
//		NotifyAllClientsToCreatePlayer(peer);
//	}

//	// 服务器端 发送 新客户端：连接成功消息
//	private void SendConnectionSuccessMessage(NetPeer peer) {
//		NetDataWriter writer = new NetDataWriter();
//		writer.Put((int)PacketType.ConnectedToServer);
//		writer.Put(_server.ConnectedPeersCount - 1);

//		foreach (NetPeer connectedPeer in _server.ConnectedPeerList) {
//			if ((int)connectedPeer.Tag == (int)peer.Tag) continue;
//			writer.Put((int)connectedPeer.Tag);
//		}

//		peer.Send(writer, DeliveryMethod.ReliableOrdered);
//	}

//	// 发送世界种子
//	private void SendWorldSeedToPeer() {
//		NetDataWriter writer = new NetDataWriter();
//		writer.Put((int)PacketType.SeedUpdate);
//		writer.Put(WorldLoader.instance.seed);

//		// 触发Steam数据发送
//		// 转为byte[]
//		// 使用可靠发送
//		NetworkEvents.TriggerSendSteamHostData(
//			MPDataSerializer.WriterToBytes(writer),
//			SendType.Reliable);
//	}

//	// 服务器端 通知 客户端：通知新玩家创建
//	private void NotifyAllClientsToCreatePlayer(NetPeer peer) {
//		NetDataWriter writer = new NetDataWriter();
//		writer.Put((int)PacketType.CreatePlayer);
//		writer.Put((int)peer.Tag);
//		_server.SendToAll(writer, DeliveryMethod.ReliableOrdered, peer);
//	}

//	// 服务器端 处理 客户端：处理网络数据接收
//	private void ProcessNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
//		// 基本验证：确保数据足够读取一个整数(数据包类型)
//		if (reader.AvailableBytes < 4) {
//			reader.Recycle();
//			return;
//		}

//		int packetType = reader.GetInt();

//		switch (packetType) {
//			case (int)PacketType.PlayerDataUpdate:
//				// 广播玩家位置
//				BroadcastPlayerTransform(peer, reader);
//				break;
//		}

//		reader.Recycle();
//	}

//	// 服务器端 广播 客户端：转发位置更新
//	private void BroadcastPlayerTransform(NetPeer peer, NetPacketReader reader) {
//		NetDataWriter writer = new NetDataWriter();
//		writer.Put((int)PacketType.PlayerDataUpdate);
//		writer.Put((int)peer.Tag);
//		writer.Put(reader.GetRemainingBytes());
//		_server.SendToAll(writer, DeliveryMethod.ReliableOrdered, peer);
//	}

//	// 服务器端 广播 客户端: 转发手部位置
//	private void BroadcastHandTransform(NetPeer peer, NetPacketReader reader) {
//		NetDataWriter writer = new NetDataWriter();
//		writer.Put((int)PacketType.PlayerDataUpdate);
//		writer.Put((int)peer.Tag);
//		writer.Put(reader.GetRemainingBytes());
//		_server.SendToAll(writer, DeliveryMethod.ReliableOrdered, peer);
//	}

//	// 服务器端 处理 客户端：断开连接
//	private void HandlePeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
//		int disconnectedPlayerId = (int)peer.Tag;

//		// 主机端：销毁本地的远程玩家代理对象
//		if (_remotePlayerObjects.ContainsKey(disconnectedPlayerId)) {
//			Destroy(_remotePlayerObjects[disconnectedPlayerId]);
//			_remotePlayerObjects.Remove(disconnectedPlayerId);
//			MPMain.Logger.LogInfo("[MP Mod server] 主机已移除远程玩家 ID: " + disconnectedPlayerId);
//		}

//		// 通知所有剩余的客户端移除该玩家
//		NotifyAllClientsToRemovePlayer(disconnectedPlayerId);
//	}

//	// 服务器端 通知 客户端：移除玩家
//	private void NotifyAllClientsToRemovePlayer(int playerId) {
//		NetDataWriter writer = new NetDataWriter();
//		writer.Put((int)PacketType.RemovePlayer);
//		writer.Put(playerId);
//		_server.SendToAll(writer, DeliveryMethod.ReliableOrdered);
//	}

//	// 命令实现
//	public void Host(string[] args) {
//		// 先关闭现有连接
//		CloseAllConnections();

//		if (_server == null) {
//			MPMain.Logger.LogError("[MP Mod server] 服务器管理器不存在");
//			return;
//		}

//		// 修复：检查参数长度, 防止 IndexOutOfRangeException
//		if (args.Length < 1) {
//			CommandConsole.LogError(
//				"Usage: host <port> [max_players]\nExample: host 22222");
//			return;
//		}

//		ushort port = ushort.Parse(args[0]);

//		if (args.Length >= 2) {
//			_maxPlayerCount = int.Parse(args[1]);
//		} else {
//			_maxPlayerCount = 4; // 默认值
//		}

//		_server.Start(port); // 在指定端口启动服务器

//		// 订阅服务器事件
//		_serverListener.ConnectionRequestEvent += ProcessConnectionRequest;
//		_serverListener.PeerConnectedEvent += ProcessPeerConnected;
//		_serverListener.NetworkReceiveEvent += ProcessNetworkReceive;
//		_serverListener.PeerDisconnectedEvent += HandlePeerDisconnected;

//		// 主机作为客户端连接到自己的服务器
//		Join(["127.0.0.1", port.ToString()]);

//		MPMain.Logger.LogInfo("[MP Mod server] 已创建服务端");

//		CommandConsole.Log("Hosting lobby...");
//		CommandConsole.LogError(
//			"You are a hosting a peer-to-peer lobby\n"
//			+ "By sharing your IP you are also sharing your address\n"
//			+ "Be careful... :)");
//	}

//	public void Join(string[] args) {
//		// 先取消可能存在的客户端订阅
//		_clientListener.NetworkReceiveEvent -= ProcessClientNetworkReceive;

//		if (_client == null) {
//			MPMain.Logger.LogError("[MP Mod client] 客户端管理器不存在");
//			return;
//		}

//		// 参数验证
//		if (args.Length < 2) {
//			CommandConsole.LogError(
//				"Usage: join <IP> <port>\n"
//				+ "Example: join 127.0.0.1 22222 or join [::1] 22222");
//			return;
//		}

//		// 解析IP地址和端口
//		string ip = args[0];
//		int port = int.Parse(args[1]);

//		// 如果客户端已经在运行, 先停止
//		if (_client.IsRunning) {
//			_client.DisconnectAll();
//			_client.Stop();
//		}

//		// 启动客户端并连接到服务器
//		_client.Start();
//		_serverPeer = _client.Connect(ip, port, "");

//		// 设置多人游戏活动标志
//		MultiPlayerCore.IsMultiplayerActive = true;

//		// 处理客户端接收到的网络数据
//		_clientListener.NetworkReceiveEvent += ProcessClientNetworkReceive;

//		MPMain.Logger.LogInfo("[MP Mod server] 尝试连接: " + ip);
//		CommandConsole.Log("Trying to join ip: " + ip);
//	}

//	public void Leave(string[] args) {
//		CloseAllConnections();
//		MPMain.Logger.LogInfo("[MP Mod] 所有连接已断开, 远程玩家已清理.");
//	}

//	public void ChaosMod(string[] args) {
//		if (args.Length <= 0) {
//			IsChaosMod = !IsChaosMod;
//		} else {
//			try {
//				IsChaosMod = TypeConverter.ToBool(args[0]);
//			} catch {
//				CommandConsole.LogError("Usage: chaos <bool> \n bool value can be: true false 1 0");
//			}
//		}
//	}
//}
