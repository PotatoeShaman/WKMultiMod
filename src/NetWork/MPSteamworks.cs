using LiteNetLib.Utils;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WKMultiMod.src.Core;
using WKMultiMod.src.Data;

namespace WKMultiMod.src.NetWork;

public class MPSteamworks : MonoBehaviour {
	// 当前大厅（只读，用于查询）
	private Lobby _currentLobby;

	// 网络管理器（只做连接，不做业务逻辑）
	private SocketManager _socketManager;
	private Dictionary<SteamId, ConnectionManager> _outgoingConnections = new Dictionary<SteamId, ConnectionManager>();
	private Dictionary<SteamId, Connection> _allConnections = new Dictionary<SteamId, Connection>();

	// 消息队列
	private ConcurrentQueue<NetworkMessage> _messageQueue = new ConcurrentQueue<NetworkMessage>();

	// 辅助属性
	public bool IsHost => _socketManager != null;

	void Awake() {
		Initialize();

		// 订阅发送事件
		SteamNetworkEvents.OnSendToHost += HandleSendToHost;
		SteamNetworkEvents.OnHostBroadcast += HandleHostBroadcast;
		SteamNetworkEvents.OnHostSendToPeer += HandleHostSendToPeer;

		// 订阅主机专用事件
		SteamNetworkEvents.OnHostRequestExistingPlayers += HandleHostRequestExistingPlayers;
	}

	void Update() {
		ProcessMessageQueue();

		if (_socketManager != null) {
			_socketManager.Receive(32);
		}

		foreach (var connectionManager in _outgoingConnections.Values) {
			connectionManager.Receive(32);
		}
	}

	void OnDestroy() {
		// 取消订阅
		SteamNetworkEvents.OnSendToHost -= HandleSendToHost;
		SteamNetworkEvents.OnHostBroadcast -= HandleHostBroadcast;
		SteamNetworkEvents.OnHostSendToPeer -= HandleHostSendToPeer;
		SteamNetworkEvents.OnHostRequestExistingPlayers -= HandleHostRequestExistingPlayers;

		DisconnectAll();
	}

	/// <summary>
	/// 初始化（只做网络层初始化）
	/// </summary>
	private void Initialize() {
		try {
			if (!SteamClient.IsValid) {
				MPMain.Logger.LogError("[MP Mod] Steamworks初始化失败！");
				return;
			}

			MPMain.Logger.LogInfo($"[MP Mod] Steamworks初始化成功！玩家: {SteamClient.Name} ID: {SteamClient.SteamId.Value}");

			// 订阅大厅事件（只做事件转发）
			SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
			SteamMatchmaking.OnLobbyMemberJoined += OnLobbyMemberJoined;
			SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeft;
			SteamMatchmaking.OnLobbyMemberDisconnected += OnLobbyMemberDisconnected;

			// 初始化中继网络（必须调用）
			SteamNetworkingUtils.InitRelayNetworkAccess();

		} catch (Exception ex) {
			MPMain.Logger.LogError($"[MP Mod] Steamworks初始化异常: {ex.Message}");
		}
	}

	/// <summary>
	/// 断开所有连接（清理网络资源）
	/// </summary>
	private void DisconnectAll() {
		// 断开所有出站连接
		foreach (var connectionManager in _outgoingConnections.Values) {
			try {
				connectionManager.Close();
			} catch { }
		}
		_outgoingConnections.Clear();

		// 关闭监听Socket
		if (_socketManager != null) {
			try {
				_socketManager.Close();
			} catch { }
			_socketManager = null;
		}

		// 清理所有连接记录
		_allConnections.Clear();

		// 离开大厅（如果有）
		if (_currentLobby.Id.IsValid) {
			try {
				_currentLobby.Leave();
			} catch { }
			_currentLobby = default;
		}

		// 清空消息队列
		while (_messageQueue.TryDequeue(out _)) { }

		MPMain.Logger.LogInfo("[MP Mod] 所有网络连接已断开");
	}

	/// <summary>
	/// 客机: 处理发送给主机的数据
	/// </summary>
	private void HandleSendToHost(byte[] data, SendType sendType, ushort laneIndex) {
		if (_currentLobby.Id.IsValid) {
			var hostSteamId = _currentLobby.Owner.Id;
			if (hostSteamId != SteamClient.SteamId && _allConnections.TryGetValue(hostSteamId, out var connection)) {
				connection.SendMessage(data, sendType, laneIndex);
			}
		}
	}

	/// <summary>
	/// 主机: 处理广播给所有玩家的数据
	/// </summary>
	private void HandleHostBroadcast(byte[] data, SendType sendType, ushort laneIndex) {
		foreach (var connection in _allConnections.Values) {
			try {
				connection.SendMessage(data, sendType, laneIndex);
			} catch (Exception ex) {
				MPMain.Logger.LogError($"[MP Mod] 广播数据异常: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// 主机: 向特定玩家发送数据
	/// </summary>
	/// <param name="data"></param>
	/// <param name="steamId"></param>
	/// <param name="sendType"></param>
	/// <param name="laneIndex"></param>
	private void HandleHostSendToPeer(byte[] data, SteamId steamId,
		SendType sendType, ushort laneIndex) {
		try {
			_allConnections[steamId].SendMessage(data, sendType, laneIndex);
		} catch (Exception ex) {
			MPMain.Logger.LogError($"[MP Mod] 单播数据异常: {ex.Message} 目标: {steamId.Value}");
		}
	}

	/// <summary>
	/// 接收网络消息并转发到总线
	/// </summary>
	public void ReceiveNetworkMessage(SteamId senderId, byte[] data) {
		_messageQueue.Enqueue(new NetworkMessage {
			SenderId = senderId,
			Data = data,
			ReceiveTime = DateTime.UtcNow
		});
	}

	/// <summary>
	/// 处理消息队列
	/// </summary>
	private void ProcessMessageQueue() {
		int processedCount = 0;
		const int maxMessagesPerFrame = 50;

		while (processedCount < maxMessagesPerFrame && _messageQueue.TryDequeue(out var message)) {
			try {
				// 直接转发到总线，不处理业务逻辑
				SteamNetworkEvents.TriggerReceiveSteamData((int)message.SenderId.Value, message.Data);
				processedCount++;
			} catch (Exception ex) {
				MPMain.Logger.LogError($"[MP Mod] 转发消息异常: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// 玩家连接成功 - 发布到总线
	/// </summary>
	public void OnPlayerConnected(SteamId steamId, Connection connection, bool isIncoming) {
		MPMain.Logger.LogInfo($"[MP Mod] 玩家连接成功: {steamId.Value}");

		if (isIncoming) {
			// 入站连接（其他玩家连接到我的监听Socket）
			// 如果我是主机，需要通知新玩家所有现有玩家信息
			if (_socketManager != null) {
				// 发送连接成功消息给新玩家
				SendConnectionSuccessToNewPlayer(steamId);
			}
		}

		// 记录连接
		_allConnections[steamId] = connection;

		// 发布连接事件到总线
		SteamNetworkEvents.TriggerSteamPlayerConnected(steamId);
	}

	/// <summary>
	/// 玩家断开连接 - 发布到总线
	/// </summary>
	public void OnPlayerDisconnected(SteamId playerId) {
		MPMain.Logger.LogInfo($"[MP Mod] 玩家断开连接: {playerId.Value}");

		// 清理连接
		_outgoingConnections.Remove(playerId);
		_allConnections.Remove(playerId);

		// 发布断开事件到总线
		SteamNetworkEvents.TriggerSteamPlayerDisconnected(playerId.Value);
	}

	/// <summary>
	/// 处理主机请求现有玩家列表
	/// </summary>
	private void HandleHostRequestExistingPlayers(SteamId newPlayerSteamId) {
		if (!IsHost) return;

		// 获取所有连接中的SteamId（除了新玩家）
		var existingSteamIds = new List<SteamId>();
		foreach (var steamId in _allConnections.Keys) {
			if (steamId != newPlayerSteamId && steamId != SteamClient.SteamId) {
				existingSteamIds.Add(steamId);
			}
		}

		// 需要Core将SteamId转换为PlayerId
		// 这里只是记录，实际转换在Core中完成
		MPMain.Logger.LogInfo($"[MP Mod] 主机获取到 {existingSteamIds.Count} 个现有玩家连接");
	}

	/// <summary>
	/// 连接到指定玩家（纯网络连接，不处理业务逻辑）
	/// </summary>
	private void ConnectToPlayer(SteamId playerId) {
		try {
			if (_outgoingConnections.ContainsKey(playerId) || _allConnections.ContainsKey(playerId)) {
				MPMain.Logger.LogWarning($"[MP Mod] 已经连接到玩家: {playerId.Value}");
				return;
			}

			var connectionManager = SteamNetworkingSockets.ConnectRelay<SteamConnectionManager>(playerId, 0);
			_outgoingConnections[playerId] = connectionManager;
			_allConnections[playerId] = connectionManager.Connection;

			MPMain.Logger.LogInfo($"[MP Mod] 正在连接玩家: {playerId.Value}");
		} catch (Exception ex) {
			MPMain.Logger.LogError($"[MP Mod] 连接玩家异常: {ex.Message}");
		}
	}

	/// <summary>
	/// 创建房间（主机模式）
	/// </summary>
	public async Task<bool> CreateRoom(string roomName, int maxPlayers = 4) {
		try {
			MPMain.Logger.LogInfo($"[MP Mod] 正在创建房间: {roomName}");

			// 先关闭现有连接
			DisconnectAll();

			// 创建大厅
			var lobbyResult = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);

			if (!lobbyResult.HasValue) {
				MPMain.Logger.LogError("[MP Mod] 创建房间失败");
				return false;
			}

			_currentLobby = lobbyResult.Value;

			// 设置大厅数据（基本配置）
			_currentLobby.SetData("name", roomName);
			_currentLobby.SetData("game_version", Application.version);
			_currentLobby.SetData("owner", SteamClient.SteamId.ToString());

			_currentLobby.SetPublic();
			_currentLobby.SetJoinable(true);
			_currentLobby.Owner = new Friend(SteamClient.SteamId);

			// 创建监听Socket（作为主机）
			_socketManager = SteamNetworkingSockets.CreateRelaySocket<SteamSocketManager>();

			MPMain.Logger.LogInfo($"[MP Mod] 房间创建成功，ID: {_currentLobby.Id.Value}");
			return true;

		} catch (Exception ex) {
			MPMain.Logger.LogError($"[MP Mod] 创建房间异常: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// 加入房间（客户端模式）
	/// </summary>
	public async Task<bool> JoinRoom(ulong lobbyId) {
		try {
			MPMain.Logger.LogInfo($"[MP Mod] 正在加入房间: {lobbyId}");

			// 先关闭现有连接
			DisconnectAll();

			var lobbyResult = await SteamMatchmaking.JoinLobbyAsync(new SteamId { Value = lobbyId });

			if (!lobbyResult.HasValue) {
				MPMain.Logger.LogError("[MP Mod] 加入房间失败");
				return false;
			}

			_currentLobby = lobbyResult.Value;

			string roomName = _currentLobby.GetData("name") ?? "未知房间";
			MPMain.Logger.LogInfo($"[MP Mod] 加入房间成功: {roomName}");

			// 获取房主ID并连接
			var hostSteamId = _currentLobby.Owner.Id;

			if (hostSteamId != SteamClient.SteamId) {
				// 连接到房主
				ConnectToPlayer(hostSteamId);
			}

			return true;

		} catch (Exception ex) {
			MPMain.Logger.LogError($"[MP Mod] 加入房间异常: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// 大厅进入 - 发布到总线
	/// </summary>
	private void OnLobbyEntered(Lobby lobby) {
		_currentLobby = lobby;
		MPMain.Logger.LogInfo($"[MP Mod] 进入大厅: {lobby.Id.Value}");

		// 发布事件到总线
		SteamNetworkEvents.TriggerLobbyEntered(lobby);
	}

	/// <summary>
	/// 大厅成员加入 - 发布到总线
	/// </summary>
	private void OnLobbyMemberJoined(Lobby lobby, Friend friend) {
		if (lobby.Id == _currentLobby.Id) {
			MPMain.Logger.LogInfo($"[MP Mod] 玩家加入房间: {friend.Name}");

			// 发布事件到总线
			SteamNetworkEvents.TriggerLobbyMemberJoined(friend.Id);

			// 如果是主机，连接到新玩家
			if (friend.Id != SteamClient.SteamId && _socketManager != null) {
				ConnectToPlayer(friend.Id);
			}
		}
	}

	/// <summary>
	/// 大厅成员离开 - 发布到总线
	/// </summary>
	private void OnLobbyMemberLeft(Lobby lobby, Friend friend) {
		if (lobby.Id == _currentLobby.Id) {
			MPMain.Logger.LogInfo($"[MP Mod] 玩家离开房间: {friend.Name}");

			// 发布事件到总线
			SteamNetworkEvents.TriggerLobbyMemberLeft(friend.Id);

			// 断开连接
			OnPlayerDisconnected(friend.Id);
		}
	}

	/// <summary>
	/// 大厅成员断开连接 - 发布到总线
	/// </summary>
	private void OnLobbyMemberDisconnected(Lobby lobby, Friend friend) {
		if (lobby.Id == _currentLobby.Id) {
			MPMain.Logger.LogInfo($"[MP Mod] 玩家断开连接: {friend.Name}");

			// 发布断开事件到总线
			SteamNetworkEvents.TriggerSteamPlayerDisconnected(friend.Id);

			// 清理连接记录
			_outgoingConnections.Remove(friend.Id);
			_allConnections.Remove(friend.Id);
		}
	}

	/// <summary>
	/// 向新玩家发送连接成功消息（仅主机需要）
	/// </summary>
	private void SendConnectionSuccessToNewPlayer(SteamId newPlayerId) {
		// 获取所有现有玩家的SteamId（除了新玩家）
		var existingPlayerIds = new List<int>();
		foreach (var steamId in _allConnections.Keys) {
			if (steamId != newPlayerId && steamId != SteamClient.SteamId) {
				existingPlayerIds.Add((int)steamId.Value);
			}
		}

		// 通过总线发送创建玩家消息给所有现有玩家
		foreach (var steamId in _allConnections.Keys) {
			if (steamId != newPlayerId) {
				var writer = new NetDataWriter();
				writer.Put((int)PacketType.CreatePlayer);
				writer.Put((int)newPlayerId.Value);
				_allConnections[steamId].SendMessage(MPDataSerializer.WriterToBytes(writer));
			}
		}

		// 发送连接成功消息给新玩家
		var successWriter = new NetDataWriter();
		successWriter.Put((int)PacketType.ConnectedToServer);
		successWriter.Put(existingPlayerIds.Count);
		foreach (var id in existingPlayerIds) {
			successWriter.Put(id);
		}
		_allConnections[newPlayerId].SendMessage(MPDataSerializer.WriterToBytes(successWriter));
	}

	/// <summary>
	/// Steam Socket管理器 - 完全无状态
	/// </summary>
	private class SteamSocketManager : SocketManager {
		// 使用静态方法获取当前实例
		private static MPSteamworks GetInstance() {
			return GameObject.FindObjectOfType<MPSteamworks>();
		}

		public override void OnConnecting(Connection connection, ConnectionInfo info) {
			MPMain.Logger.LogInfo($"[MP Mod] 玩家正在连接: {info.Identity.SteamId.Value}");
			connection.Accept();
		}

		public override void OnConnected(Connection connection, ConnectionInfo info) {
			var instance = GetInstance();
			if (instance != null) {
				instance.OnPlayerConnected(info.Identity.SteamId, connection, true);
			}
		}

		public override void OnMessage(Connection connection, NetIdentity identity,
									  IntPtr data, int size, long messageNum,
									  long recvTime, int channel) {
			var instance = GetInstance();
			if (instance != null) {
				byte[] bytes = new byte[size];
				System.Runtime.InteropServices.Marshal.Copy(data, bytes, 0, size);
				instance.ReceiveNetworkMessage(identity.SteamId, bytes);
			}
		}

		public override void OnDisconnected(Connection connection, ConnectionInfo info) {
			var instance = GetInstance();
			if (instance != null) {
				instance.OnPlayerDisconnected(info.Identity.SteamId);
			}
		}
	}

	/// <summary>
	/// Steam 连接管理器 - 完全无状态
	/// </summary>
	private class SteamConnectionManager : ConnectionManager {
		private static MPSteamworks GetInstance() {
			return GameObject.FindObjectOfType<MPSteamworks>();
		}

		public override void OnConnecting(ConnectionInfo info) { }

		public override void OnConnected(ConnectionInfo info) {
			var instance = GetInstance();
			if (instance != null) {
				instance.OnPlayerConnected(info.Identity.SteamId, this.Connection, false);
			}
		}

		public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel) {
			var instance = GetInstance();
			if (instance != null) {
				byte[] bytes = new byte[size];
				System.Runtime.InteropServices.Marshal.Copy(data, bytes, 0, size);
				instance.ReceiveNetworkMessage(this.ConnectionInfo.Identity.SteamId, bytes);
			}
		}

		public override void OnDisconnected(ConnectionInfo info) {
			var instance = GetInstance();
			if (instance != null) {
				instance.OnPlayerDisconnected(info.Identity.SteamId);
			}
		}
	}

	/// <summary>
	/// 网络消息结构
	/// </summary>
	private struct NetworkMessage {
		public SteamId SenderId;
		public byte[] Data;
		public DateTime ReceiveTime;
	}
}