using Steamworks;
using Steamworks.Data;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.Util;

namespace WKMPMod.NetWork;

// 只做连接,不做业务逻辑
public class MPSteamworks : MonoBehaviour {
	/// <summary>
	/// 网络消息结构
	/// </summary>
	private struct NetworkMessage {
		public SteamId SenderId;
		public byte[] Data;
		public int Length;
		public DateTime ReceiveTime;
	}

	// Debug日志输出间隔
	private TickTimer _debugTick = new TickTimer(5f);
	// 大厅Id
	private Lobby _currentLobby;
	// 获取当前大厅ID
	public ulong CurrentLobbyId {
		get { return _currentLobby.Id.Value; }
	}
	// 检查是否在大厅中
	public bool IsInLobby {
		get { return _currentLobby.Id.IsValid; }
	}

	// 本机Id
	public ulong UserSteamId { get; private set; }
	// 之前的主机Id
	public ulong HostSteamId { get; private set; }
	// 广播Id
	public ulong BroadcastId { get; } = 0;
	// 特殊Id (必须解包)
	public ulong SpecialId { get; } = 1;

	// 监听socket
	internal SteamSocketManager _socketManager;
	// 出站连接池
	internal Dictionary<SteamId, SteamConnectionManager> _outgoingConnections = new Dictionary<SteamId, SteamConnectionManager>();
	// 已经建立成功的连接池
	internal Dictionary<SteamId, Connection> _allConnections = new Dictionary<SteamId, Connection>();

	// 是否有链接
	public bool HasConnections { get; private set; }

	// 消息队列
	private ConcurrentQueue<NetworkMessage> _messageQueue = new ConcurrentQueue<NetworkMessage>();
	// 数据池
	private static readonly ArrayPool<byte> _messagePool = ArrayPool<byte>.Shared;

	// 检查是否是大厅所有者
	public bool IsHost {
		get {
			if (_currentLobby.Id == 0) return false;
			return _currentLobby.Owner.Id == SteamClient.SteamId;
		}
	}

	// 获取大厅ID
	public ulong LobbyId {
		get => _currentLobby.Id.Value;
	}

	// 判断玩家是否在大厅
	public bool IsMemberInLobby(SteamId targetId) {
		foreach (var member in _currentLobby.Members) { 
			if(member.Id == targetId) return true;	
		}
		return false;
	}

	// 获取全部在线玩家
	public IEnumerable<Friend> Friends { get; private set; }

	#region[生命周期函数]
	void Awake() {

		//SteamClient.Init(3195790u);

		try {
			if (!SteamClient.IsValid) {

				MPMain.LogError(
					"[MPSW] Steamworks初始化失败,请检查Steam在线情况",
					"[MPSW] Failed to initialize Steamworks. Please check your Steam login status.");
				return;
			}

			UserSteamId = SteamClient.SteamId;
			MPMain.LogInfo(
				$"[MPSW] Steamworks初始化成功 玩家: " +
				$"玩家: {SteamClient.Name} ID: {SteamClient.SteamId.ToString()}",
				$"[MPSW] Steamworks initialization succeeded. " +
				$"Player: {SteamClient.Name} ID: {SteamClient.SteamId.ToString()}");

			// 订阅大厅事件 大部分只做转发
			// 本机加入大厅
			SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
			// 该用户已经加入或正在加入大厅
			SteamMatchmaking.OnLobbyMemberJoined += OnLobbyMemberJoined;
			// 该用户已离开或即将离开大厅
			SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeft;
			// 该用户在未离开大厅的情况下断线
			SteamMatchmaking.OnLobbyMemberDisconnected += OnLobbyMemberDisconnected;
			// 当大厅成员数据或大厅所有权发生变更
			SteamMatchmaking.OnLobbyMemberDataChanged += OnLobbyMemberDataChanged;

			// 初始化中继网络(必须调用)
			SteamNetworkingUtils.InitRelayNetworkAccess();

		} catch (Exception ex) {
			MPMain.LogError(
				$"[MPSW] Steamworks初始化异常: {ex.Message}",
				$"[MPSW] Steamworks initialization exception: {ex.Message}");
		}

	}

	void Update() {
		Steamworks.SteamClient.RunCallbacks();

		_socketManager?.Receive(32);

		foreach (var connectionManager in _outgoingConnections.Values) {
			connectionManager.Receive(32);
		}

		ProcessMessageQueue();
	}

	void OnDestroy() {
		// 订阅大厅事件 大部分只做转发
		// 本机加入大厅
		SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
		// 该用户已经加入或正在加入大厅
		SteamMatchmaking.OnLobbyMemberJoined -= OnLobbyMemberJoined;
		// 该用户已离开或即将离开大厅
		SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeft;
		// 该用户在未离开大厅的情况下断线
		SteamMatchmaking.OnLobbyMemberDisconnected -= OnLobbyMemberDisconnected;
		// 当大厅成员数据或大厅所有权发生变更
		SteamMatchmaking.OnLobbyMemberDataChanged -= OnLobbyMemberDataChanged;

		DisconnectAll();
	}

	/// <summary>
	/// 断开所有连接(清理网络资源)
	/// </summary>
	public void DisconnectAll() {
		// 断开所有出站连接
		foreach (var connectionManager in _outgoingConnections.Values) {
			connectionManager?.Close();
		}
		_outgoingConnections.Clear();

		// 关闭监听Socket
		_socketManager?.Close();
		_socketManager = null;

		// 清理所有连接记录
		_allConnections.Clear();

		// 状态重置
		HasConnections = false;
		HostSteamId = 0;

		// 离开大厅(如果有)
		if (_currentLobby.Id.IsValid) {
			try {
				_currentLobby.Leave();
			} catch { }
			_currentLobby = default;
		}

		// 清空消息队列
		while (_messageQueue.TryDequeue(out _)) { }

		MPMain.LogInfo(
			"[MPSW] 所有网络连接已断开",
			"[MPSW] All network connections have been disconnected.");
	}
	#endregion

	#region[发送数据函数]

	/// <summary>
	/// 专门为 DataWriter 准备的重载,实现零拷贝转发
	/// </summary>
	public void SendToHost(DataWriter writer, SendType sendType = SendType.Reliable, ushort laneIndex = 0) {
		// 从 Writer 获取视图(ArraySegment 是结构体,包装它是轻量级的)
		var segment = writer.Data;
		SendToHost(segment.Array, segment.Offset, segment.Count, sendType, laneIndex);
	}

	/// <summary>
	/// 发送数据: 本机->总线->主机玩家
	/// </summary>
	public void SendToHost(byte[] data, int offset, int length,
						   SendType sendType = SendType.Reliable, ushort laneIndex = 0) {

		if (_currentLobby.Id.IsValid) {
			var hostSteamId = _currentLobby.Owner.Id;
			if (hostSteamId != SteamClient.SteamId && _allConnections.TryGetValue(hostSteamId, out var connection)) {
				connection.SendMessage(data, offset, length, sendType, laneIndex);
			}
		}
	}

	/// <summary>
	/// 专门为 DataWriter 准备的重载,实现零拷贝转发
	/// </summary>
	public void Broadcast(DataWriter writer, SendType sendType = SendType.Reliable, ushort laneIndex = 0) {
		// 从 Writer 获取视图(ArraySegment 是结构体,包装它是轻量级的)
		var segment = writer.Data;
		Broadcast(segment.Array, segment.Offset, segment.Count, sendType, laneIndex);
	}

	/// <summary>
	/// 发送数据: 本机->总线->所有连接玩家
	/// </summary>
	public void Broadcast(byte[] data, int offset, int length,
						  SendType sendType = SendType.Reliable, ushort laneIndex = 0) {

		// Debug
		bool canLog = _debugTick.TryTick();
		if (canLog) {
			MPMain.LogInfo(
				$"[MPSW] 开始广播数据,当前连接数: {_allConnections.Count.ToString()}",
				$"[MPSW] Started broadcasting data, current connections: {_allConnections.Count.ToString()}");
		}

		foreach (var (steamId, connection) in _allConnections) {
			try {
				if (canLog) {
					MPMain.LogInfo(
						$"[MPSW] 广播数据,当前连接: " +
						$"SteamId: {steamId.ToString()} 连接Id: {connection.Id.ToString()}",
						$"[MPSW] Sending data to connections. " +
						$"SteamId: {steamId.ToString()} ConnectionId: {connection.Id.ToString()}");
				}

				connection.SendMessage(data, offset, length, sendType, laneIndex);
			} catch (Exception ex) {
				MPMain.LogError(
					$"[MPSW] 广播数据异常: {ex.Message}",
					$"[MPSW] Broadcasting data exception: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// 专门为 DataWriter 准备的重载,实现零拷贝转发
	/// </summary>
	public void SendToPeer(SteamId steamId, DataWriter writer, 
						   SendType sendType = SendType.Reliable, ushort laneIndex = 0) {
		// 从 Writer 获取视图(ArraySegment 是结构体,包装它是轻量级的)
		var segment = writer.Data;
		SendToPeer(steamId, segment.Array, segment.Offset, segment.Count, sendType, laneIndex);
	}

	/// <summary>
	/// 发送数据: 本机->特定玩家
	/// </summary>
	public void SendToPeer(SteamId steamId, byte[] data, int offset, int length,
						   SendType sendType = SendType.Reliable, ushort laneIndex = 0) {

		try {
			_allConnections[steamId].SendMessage(data, offset, length, sendType, laneIndex);
		} catch (Exception ex) {
			MPMain.LogError(
				$"[MPSW] 单播数据异常: {ex.Message} SteamId: {steamId.ToString()}",
				$"[MPSW] Unicast data exception: {ex.Message} SteamId: {steamId.ToString()}");
		}
	}

	/// <summary>
	/// 发送数据: 本机->除个别玩家外所有连接玩家
	/// </summary>
	/// <param name="steamId">被排除的玩家</param>
	internal void BroadcastExcept(ulong steamId, byte[] data, int offset, int length,
								  SendType sendType = SendType.Reliable, ushort laneIndex = 0) {

		foreach (var (tempSteamId, connection) in _allConnections) {
			if (steamId == tempSteamId)
				continue;
			try {
				connection.SendMessage(data, offset, length, sendType, laneIndex);
			} catch (Exception ex) {
				MPMain.LogError(
					$"[MPSW] 广播数据异常: {ex.Message}",
					$"[MPSW] Broadcasting data exception: {ex.Message}");
			}
		}
	}
	#endregion

	#region[消息处理函数]
	/// <summary>
	/// 接收数据: 任意玩家->消息队列
	/// </summary>
	private void HandleIncomingRawData(SteamId senderId, IntPtr data, int size) {
		// 从池里借出一块内存. 注意：buffer.Length 可能 >= size
		byte[] buffer = _messagePool.Rent(size);

		// 将非托管指针数据拷贝到借来的数组中
		System.Runtime.InteropServices.Marshal.Copy(data, buffer, 0, size);

		// 入队
		_messageQueue.Enqueue(new NetworkMessage {
			SenderId = senderId.Value,
			Data = buffer,
			Length = size,
			ReceiveTime = DateTime.UtcNow
		});
	}

	/// <summary>
	/// 处理消息队列: 消息队列->ReceiveSteamData总线
	/// </summary>
	private void ProcessMessageQueue() {
		int processedCount = 0;
		const int maxMessagesPerFrame = 50;

		while (processedCount < maxMessagesPerFrame && _messageQueue.TryDequeue(out var message)) {
			try {
				// 使用 ArraySegment 包装
				var segment = new ArraySegment<byte>(message.Data, 0, message.Length);

				// 触发总线
				MPEventBusNet.NotifyReceive(message.SenderId, segment);

				processedCount++;
			} catch (Exception ex) {
				MPMain.LogError(
					$"[MPSW] 消息队列转发消息异常: {ex.Message}",
					$"[MPSW] MessageQueue forwarding message to MPCore exception: {ex.Message}");

			} finally {
				// 数据归还缓冲区
				_messagePool.Return(message.Data);
			}
		}
	}
	#endregion

	#region[连接/断连 回调函数]

	/// <summary>
	/// 接收数据: 任意玩家->本机 / 本机->任意玩家 连接成功 -> Player(In/Out)Connected总线
	/// </summary>
	public void OnPlayerConnected(SteamId steamId, Connection connection, bool isIncoming) {

	}

	/// <summary>
	/// 接收数据: 玩家断开连接 -> PlayerDisconnected总线
	/// </summary>
	public void OnPlayerDisconnected(SteamId steamId) {
		if (_allConnections.ContainsKey(steamId)) {

			_allConnections.Remove(steamId);
			_outgoingConnections.Remove(steamId);

			// 重连检测
			if (IsInLobby || IsMemberInLobby(steamId))
				StartCoroutine(ConnectionController(steamId, true));

			MPMain.LogInfo(
				$"[MPSW] 玩家断开,已清理连接. SteamId: {steamId.ToString()}",
				$"[MPSW] Player disconnected, connection cleaned up. SteamId: {steamId.ToString()}");

			// 检查是否还有剩余连接
			HasConnections = _allConnections.Count > 0;

			// 没有重连成功 触发业务层销毁玩家
			if(!_allConnections.ContainsKey(steamId))
				MPEventBusNet.NotifyPlayerDisconnected(steamId);
		}
	}

	#endregion

	#region[创建/加入大厅/连接玩家函数]

	/// <summary>
	/// 创建监听socket
	/// </summary>
	public void CreateListeningSocket() {
		try {
			_socketManager = SteamNetworkingSockets.CreateRelaySocket<SteamSocketManager>(1);
			_socketManager.Instance = this;
		} catch (Exception socketEx) {
			MPMain.LogError(
				$"[MPSW] 创建Socket失败: {socketEx.Message}",
				$"[MPSW] Create Socket exception: {socketEx.Message}");
		}
	}

	/// <summary>
	/// 创建大厅(主机模式)- 异步版本
	/// </summary>
	public async Task<bool> CreateRoomAsync(string roomName, int maxPlayers) {
		// 清理全部连接
		DisconnectAll();
		//await Task.Yield();

		try {
			if (!SteamClient.IsValid) {
				MPMain.LogError(
					"[MPSW] SteamClient 无效,请检查Steam在线情况",
					"[MPSW] SteamClient is invalid. Please check your Steam login status.");
				return false;
			}

			// 核心：直接 await 任务
			Lobby? lobbyResult = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);

			// 只检查结果并返回,移除所有同步大厅设置和 Socket 创建！
			if (!lobbyResult.HasValue) {
				MPMain.LogError(
					"[MPSW] 创建大厅失败",
					"[MPSW] Failed to create lobby");
				return false;
			}

			_currentLobby = lobbyResult.Value;

			MPMain.LogInfo(
				$"[MPSW] 大厅创建成功,ID: {_currentLobby.Id.ToString()}",
				$"[MPSW] Lobby created successfully.Lobby ID: {_currentLobby.Id.ToString()}");

			// 设置大厅信息
			_currentLobby.SetData("name", roomName);
			_currentLobby.SetData("game_version", Application.version);
			_currentLobby.SetData("owner", SteamClient.SteamId.ToString());
			_currentLobby.SetPublic();
			_currentLobby.SetJoinable(true);
			_currentLobby.Owner = new Friend(SteamClient.SteamId);

			// 获取Socket
			CreateListeningSocket();

			return true; // 成功
		} catch (Exception ex) {
			MPMain.LogError(
				$"[MPSW] 创建大厅异常: {ex.Message}",
				$"[MPSW] Create Lobby exception: {ex.Message}");
			return false; // 失败
		}
	}

	/// <summary>
	/// CreateRoom 异步启动包装器
	/// </summary>
	public void CreateRoom(string roomName, int maxPlayers, Action<bool> callback) {
		// 启动异步
		StartCoroutine(RunAsync(CreateRoomAsync(roomName, maxPlayers), callback));
	}

	/// <summary>
	/// 加入大厅(客户端模式)- 异步版本
	/// </summary>
	public async Task<bool> JoinRoomAsync(Lobby lobby) {
		// 清理全部连接
		DisconnectAll();

		try {
			// 核心改变：直接 await 任务
			RoomEnter result = await lobby.Join();

			// 检查 RoomEnter 结果
			if (result != RoomEnter.Success) {
				throw new Exception($"[MPSW] Failed to join Steam lobby: {result.ToString()}");
			}

			_currentLobby = lobby;
			string roomName = _currentLobby.GetData("name")
				?? (MPConfig.LogLanguage == 0 ? "未知大厅" : "Unknown lobby");
			MPMain.LogInfo(
				$"[MPSW] 加入大厅成功: {roomName}",
				$"[MPSW] Successfully joined lobby: {roomName}");

			// 获取Socket
			CreateListeningSocket();

			return true;

		} catch (Exception ex) {
			MPMain.LogError(
				$"[MPSW] 加入大厅异常: {ex.Message}",
				$"[MPSW] Join lobby exception: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// JoinRoom 异步启动包装器
	/// </summary>
	public void JoinRoom(ulong lobbyId, Action<bool> callback) {
		Lobby lobby = new Lobby(lobbyId);
		// 使用 Unity 的扩展方法来启动 async Task
		StartCoroutine(RunAsync(JoinRoomAsync(lobby), callback));
	}

	/// <summary>
	/// 连接到指定玩家(纯网络连接,不处理业务逻辑)
	/// </summary>
	public void ConnectToPlayer(SteamId steamId) {
		try {
			if (_outgoingConnections.ContainsKey(steamId)) {
				MPMain.LogWarning(
					$"[MPSW] 已经连接过玩家. SteamId: {steamId.ToString()}",
					$"[MPSW] Already connected to player. SteamId: {steamId.ToString()}");
				return;
			}

			var connectionManager = SteamNetworkingSockets.ConnectRelay<SteamConnectionManager>(steamId, 1);
			connectionManager.Instance = this;
			_outgoingConnections[steamId] = connectionManager;
			_allConnections[steamId] = connectionManager.Connection;

			MPMain.LogInfo(
				$"[MPSW] 正在出站连接玩家. SteamId: {steamId.ToString()}",
				$"[MPSW] Connecting to player. SteamId: {steamId.ToString()}");
		} catch (Exception ex) {
			MPMain.LogError(
				$"[MPSW] 连接玩家异常: {ex.Message}",
				$"[MPSW] Exception while connecting to player: {ex.Message}");
		}
	}

	/// <summary>
	/// 连接到指定玩家 - 异步版本
	/// </summary>
	public async Task<bool> ConnectToPlayerAsync(SteamId steamId) {
		SteamConnectionManager connectionManager = null;
		float timeout = 5f;
		float startTime = Time.time;

		// 初始检查
		if (_outgoingConnections.ContainsKey(steamId) || _allConnections.ContainsKey(steamId)) {
			MPMain.LogWarning(
				$"[MPSW] 已经连接到玩家. SteamId: {steamId.ToString()}",
				$"[MPSW] Already connected to player. SteamId: {steamId.ToString()}");
			return true;
		}

		// 1. 同步建立连接
		try {
			MPMain.LogInfo(
				$"[MPSW] 正在连接玩家. SteamId: {steamId.ToString()}",
				$"[MPSW] Connecting to player. SteamId: {steamId.ToString()}");

			// 建立连接
			connectionManager = SteamNetworkingSockets.ConnectRelay<SteamConnectionManager>(steamId, 1);
			connectionManager.Instance = this;
			_outgoingConnections[steamId] = connectionManager;
			_allConnections[steamId] = connectionManager.Connection;

		} catch (Exception ex) {
			MPMain.LogError(
				$"[MPSW] 建立连接异常: {ex.Message}",
				$"[MPSW] Exception while connecting to player: {ex.Message}");
			return false;
		}

		// 2. 异步等待连接建立
		if (connectionManager != null) {
			while (connectionManager.ConnectionInfo.State != ConnectionState.Connected) {
				if (Time.time - startTime > timeout) {
					MPMain.LogError(
						$"[MPSW] 连接玩家超时. SteamId: {steamId.ToString()}",
						$"[MPSW] Connection to player timed out. SteamId: {steamId.ToString()}");
					_outgoingConnections.Remove(steamId);
					_allConnections.Remove(steamId);
					return false;
				}
				// 替换 yield return null
				await Task.Yield();
			}
		} else {
			return false;
		}

		MPMain.LogInfo(
			$"[MPSW] 连接玩家成功. SteamId: {steamId.ToString()}",
			$"[MPSW] Successfully connected to player. SteamId: {steamId.ToString()}");
		return true;
	}

	/// <summary>
	/// 这是一个通用的辅助方法,用于将 async Task<bool> 包装到 Unity 的 StartCoroutine 中,
	/// 并将结果传递给 Action<bool> 回调.
	/// </summary>
	private IEnumerator RunAsync(Task<bool> task, Action<bool> callback) {
		// 等待 Task 完成
		yield return new WaitWhile(() => !task.IsCompleted);

		// 强制等待一帧,确保 Task 内部的上下文完全释放
		yield return null;

		if (task.IsFaulted) {
			MPMain.LogError(
				$"[MPSW] 异步任务执行失败: {task.Exception.InnerException.Message}",
				$"[MPSW] Async task execution failed: {task.Exception.InnerException.Message}");
			callback?.Invoke(false);
		} else {
			// Task.Result 即为异步方法的返回值 (bool)
			callback?.Invoke(task.Result);
		}
	}
	#endregion

	#region[SteamMatchmaking事件处理函数]
	/// <summary>
	/// 接收数据: 进入到大厅->LobbyEntered总线
	/// </summary>
	private void OnLobbyEntered(Lobby lobby) {
		_currentLobby = lobby;
		HostSteamId = lobby.Owner.Id;
		MPMain.LogInfo(
			$"[MPSW] 进入大厅. 大厅Id: {lobby.Id.ToString()}",
			$"[MPSW] Entered lobby. LobbyId: {lobby.Id.ToString()}");

		// 在这里连接所有玩家
		// 遍历大厅里已经在的所有成员
		foreach (var member in lobby.Members) {
			if (member.Id == UserSteamId) continue; // 跳过自己
			MPMain.LogInfo(
				$"[MPCore] 连接已在大厅玩家: {member.Name}({member.Id.ToString()})",
				$"[MPCore] Connected to player in the lobby: {member.Name}({member.Id.ToString()})");
			StartCoroutine(ConnectionController(member.Id, false));
		}

		// 发布事件到总线
		MPEventBusNet.NotifyLobbyEntered(lobby);
	}

	/// <summary>
	/// 接收数据: 大厅有成员加入->LobbyMemberJoined总线->连接新玩家
	/// </summary>
	private void OnLobbyMemberJoined(Lobby lobby, Friend friend) {
		if (lobby.Id == _currentLobby.Id) {
			_currentLobby = lobby;
			MPMain.LogInfo(
				$"[MPSW] 玩家加入大厅. SteamId: {friend.Name}",
				$"[MPSW] Player joined room. SteamId: {friend.Name}");

			// 发布事件到总线
			MPEventBusNet.NotifyLobbyMemberJoined(friend.Id);

			// 连接到新玩家
			if (friend.Id != SteamClient.SteamId) {
				StartCoroutine(ConnectionController(friend.Id, false));
			}
		}
	}

	/// <summary>
	/// 接收数据: 大厅有成员离开->LobbyMemberLeft总线
	/// </summary>
	private void OnLobbyMemberLeft(Lobby lobby, Friend friend) {
		if (lobby.Id == _currentLobby.Id) {
			_currentLobby = lobby;
			MPMain.LogInfo(
				$"[MPSW] 玩家离开大厅. SteamId: {friend.Name}",
				$"[MPSW] Player left the room. SteamId: {friend.Name}");

			// 发布事件到总线
			MPEventBusNet.NotifyLobbyMemberLeft(friend.Id);

			// 只在这里处理连接清理
			OnPlayerDisconnected(friend.Id);
		}
	}

	/// <summary>
	/// 接收数据: 大厅有成员断开连接-> 总线
	/// </summary>
	private void OnLobbyMemberDisconnected(Lobby lobby, Friend friend) {
		if (lobby.Id == _currentLobby.Id) {
			_currentLobby = lobby;
			MPMain.LogInfo(
				$"[MPSW] 玩家断开大厅连接. SteamId: {friend.Name}",
				$"[MPSW] Player disconnected from the lobby. SteamId: {friend.Name}");

			// 重复分发
			// 发布断开事件到总线
			//SteamNetworkEvents.TriggerPlayerDisconnected(friend.Id);
		}
	}

	/// <summary>
	/// 接收数据: 大厅数据变更->
	/// 主机变更->LobbyHostChanged总线
	/// </summary>
	/// <param name="lobby"></param>
	/// <param name="friend"></param>
	private void OnLobbyMemberDataChanged(Lobby lobby, Friend friend) {
		// 还是原房间
		if (lobby.Id == _currentLobby.Id) {
			// 更新部分房间数据
			_currentLobby = lobby;
			// 获取当前大厅真正的主机(Owner)
			SteamId currentOwnerId = lobby.Owner.Id;
			// 检查所有权是否发生了变更
			if (HostSteamId != 0 && HostSteamId != currentOwnerId) {
				MPMain.LogInfo(
					$"[MPCore] 主机变更: {HostSteamId.ToString()} -> {currentOwnerId.ToString()}",
					$"[MPCore] Host change: {HostSteamId.ToString()} -> {currentOwnerId.ToString()}");

				// 触发主机变更总线
				MPEventBusNet.NotifyLobbyHostChanged(lobby, HostSteamId);
			}
			HostSteamId = currentOwnerId;
		}
	}
	#endregion

	#region[重连机制]

	/// <summary>
	/// 通用的连接控制器：支持初始连接和断线重连
	/// </summary>
	private IEnumerator ConnectionController(SteamId targetId, bool isReconnect) {
		yield return new WaitForSeconds(isReconnect ? 1.5f : 0.2f);
		if (!IsInLobby || !IsMemberInLobby(targetId)) yield break;

		// 核心重用逻辑：尝试并验证连接
		IEnumerator AttemptAndVerify(int maxAttempts) {
			for (int i = 0; i < maxAttempts; i++) {
				// 检查现有连接（可能在循环开始前已连上）
				if (_allConnections.ContainsKey(targetId)) {
					yield return new WaitForSeconds(1.0f);
					if (_allConnections.ContainsKey(targetId)) {
						HasConnections = true;
						MPEventBusNet.NotifyPlayerConnected(targetId);
						yield break; // 成功退出
					}
				}

				ExecuteConnection(targetId);

				// 等待重试间隔，期间持续检查状态
				float timer = 0;
				while (timer < 3.0f) { // 3秒重试间隔
					if (_allConnections.ContainsKey(targetId)) {
						yield return new WaitForSeconds(1.0f);
						if (_allConnections.ContainsKey(targetId)) {
							HasConnections = true;
							MPEventBusNet.NotifyPlayerConnected(targetId);
							yield break; // 成功退出
						}
					}
					timer += Time.deltaTime;
					yield return null;
				}
				MPMain.LogWarning($"[MPSW] 第 {i + 1} 次尝试连接 {targetId} 失败");
			}
		}

		bool isInitiator = UserSteamId < targetId;

		if (isInitiator) {
			ExecuteConnection(targetId);
			HasConnections = true;

			//MPMain.LogInfo($"[MPSW] 发起者连接流程开始 -> {targetId}");
			//yield return AttemptAndVerify(3);
		} else {


			//MPMain.LogInfo($"[MPSW] 接收者等待流程开始 <- {targetId}");
			//float waitTimer = 0;
			//bool alreadyConnected = false;

			//while (waitTimer < 10f) {
			//	if (_allConnections.ContainsKey(targetId)) {
			//		yield return new WaitForSeconds(1.0f);
			//		if (_allConnections.ContainsKey(targetId)) {
			//			HasConnections = true;
			//			MPEventBusNet.NotifyPlayerConnected(targetId);
			//			alreadyConnected = true;
			//			break;
			//		}
			//	}
			//	waitTimer += Time.deltaTime;
			//	yield return null;
			//}

			//if (!alreadyConnected) {
			//	MPMain.LogWarning($"[MPSW] 10秒超时, 开始反向尝试...");
			//	yield return AttemptAndVerify(3);
			//}
		}
	}

	// 清理连接并重连玩家
	private void ExecuteConnection(SteamId targetId) {
		// 先物理清理, 防止 Already connected 错误
		if (_outgoingConnections.TryGetValue(targetId, out var oldMgr)) {
			oldMgr.Connection.Close();
			_outgoingConnections.Remove(targetId);
		}
		_allConnections.Remove(targetId);

		ConnectToPlayer(targetId);
	}

	#endregion

	#region[Steam 监听socket管理器]
	internal class SteamSocketManager : SocketManager {

		private MPSteamworks _instance;
		public MPSteamworks Instance { get => _instance; set => _instance = value; }

		// 有玩家正在接入
		public override void OnConnecting(Connection connection, ConnectionInfo info) {
			MPMain.LogInfo(
				$"[MPSW] 玩家正在连接. SteamId: {info.Identity.SteamId.ToString()}",
				$"[MPSW] Player is connecting. SteamId: {info.Identity.SteamId.ToString()}");
			connection.Accept();
		}

		// 有玩家已经接入
		public override void OnConnected(Connection connection, ConnectionInfo info) {
			var steamId = info.Identity.SteamId;
			MPMain.LogInfo(
				$"[MPSW] 玩家已经接入. SteamId: {steamId.ToString()} " +
				$"连接Id: {connection.Id} 连接状态: {info.State}",
				$"[MPSW] The player has already connected. SteamId{steamId.ToString()}" +
				$"Connection Id: {connection.Id} Connection state: {info.State}");
			Instance?._allConnections[steamId] = connection;
			Instance?.HasConnections = true;
		}

		public override void OnConnectionChanged(Connection connection, ConnectionInfo info) {
			base.OnConnectionChanged(connection, info);
		}

		// 接收消息
		public override void OnMessage(Connection connection, NetIdentity identity,
									   IntPtr data, int size, long messageNum,
									   long recvTime, int channel) {
			Instance?.HandleIncomingRawData(identity.SteamId, data, size);
		}

		// 连接被本地或远程关闭
		public override void OnDisconnected(Connection connection, ConnectionInfo info) {
			MPMain.LogError(
				$"[MPSW] 连接断开详情: {info.ToString()}",
				$"[MPSW] Disconnected details: {info.ToString()}");
			Instance?.OnPlayerDisconnected(info.Identity.SteamId);
		}
	}
	#endregion

	#region[Steam 连接Connection管理器]
	internal class SteamConnectionManager : ConnectionManager {

		private MPSteamworks _instance;
		public MPSteamworks Instance { get => _instance; set => _instance = value; }

		// 正在去连接
		public override void OnConnecting(ConnectionInfo info) { }

		// 连接已建立
		public override void OnConnected(ConnectionInfo info) {
			MPMain.LogInfo(
				$"[MPSW] 已经主动连接玩家. SteamId: {info.Identity.SteamId.ToString()} 连接状态: {info.State}",
				$"[MPSW] Already actively connected to the player. SteamId{info.Identity.SteamId.ToString()}" +
				$"Connection state: {info.State}");
			Instance?.HasConnections = true;
		}

		// 接收消息
		public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel) {
			Instance?.HandleIncomingRawData(this.ConnectionInfo.Identity.SteamId, data, size);
		}

		// 连接被本地或远程关闭
		public override void OnDisconnected(ConnectionInfo info) {
			Instance?.OnPlayerDisconnected(info.Identity.SteamId);
		}
	}
	#endregion


}