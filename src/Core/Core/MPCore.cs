using Steamworks;
using Steamworks.Data;
using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using WKMPMod.Component;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.RemotePlayer;
using WKMPMod.Util;
using static WKMPMod.Data.MPReaderPool;
using static WKMPMod.Data.MPWriterPool;

namespace WKMPMod.Core;

#region[多人模式状态枚举]
[Flags]
public enum MPStatus {
	NotInitialized = 0b0,    // 未初始化
	Initialized = 0b1,       // 已初始化

	NotInLobby = 0b00_0,     // 未加入大厅
	JoiningLobby = 0b01_0,   // 正在加入大厅
	InLobby = 0b10_0,        // 已加入大厅
	LobbyConnectionError = 0b11_0,// 大厅连接错误

	INIT_MASK = 0b1,    // 初始化掩码
	LOBBY_MASK = 0b11_0,// 大厅状态掩码
}

public static class MPStatusExtension {
	// 设置特定字段
	public static MPStatus SetField(this ref MPStatus status, MPStatus mask, MPStatus value) {
		// 清除原有值,设置新值
		return status = (status & ~mask) | (value & mask);
	}

	// 获取特定字段
	public static MPStatus GetField(this MPStatus status, MPStatus mask) {
		return status & mask;
	}

	public static bool IsInLobby(this MPStatus status) {
		return GetField(status, MPStatus.LOBBY_MASK) == MPStatus.InLobby
			|| GetField(status, MPStatus.LOBBY_MASK) == MPStatus.JoiningLobby;
	}

	public static bool IsInitialized(this MPStatus status) {
		return GetField(status, MPStatus.INIT_MASK) == MPStatus.Initialized;
	}
}
#endregion
public class MPCore : MonoSingleton<MPCore> {

	// Debug日志输出间隔
	private TickTimer _debugTick = new TickTimer(5f);
	// 玩家数量同步间隔
	private TickTimer _syncTick = new TickTimer(3f);

	// Steam网络管理器 本地数据获取类
	private MPSteamworks _MPsteamworks;
	private RPManager _RPManager;
	private LocalPlayer _LocalPlayer;

	// 世界种子 - 用于同步游戏世界生成
	public int WorldSeed { get; private set; }
	// 多人模式状态
	public static MPStatus MultiPlayerStatus = MPStatus.NotInitialized;
	// 是否处于大厅中
	public static bool IsInLobby => MultiPlayerStatus.IsInLobby();
	public static bool IsInitialized => MultiPlayerStatus.IsInitialized();

	// 手部皮肤 -> 玩家模型ID 映射字典
	public static readonly Dictionary<string, string> HandSkinToModelId = new() {
		{ "default","default"},
		{ MPMain.SlugcatHandId, MPMain.SlugcatBodyFactoryId },
		// 可在此添加更多映射
	};

	// 注意:日志通过 MultiPlayerMain.Logger 访问
	#region[Unity组件生命周期函数]
	protected override void Awake() {
		base.Awake();
		// Debug
		MPMain.LogInfo(Localization.Get("MPCore", "Awake"));
	}

	void Start() {
		// 订阅场景切换
		SceneManager.sceneLoaded += OnSceneLoaded;

		// 初始化网络监听器和远程玩家管理器
		InitializeAllManagers();
	}

	void Update() {
		LocalPlayer.Instance.ShouldSendData = IsInLobby && IsInitialized && MPSteamworks.Instance.HasConnections;

		CheckAndRepairPlayers();
	}

	/// <summary>
	/// 当核心对象被销毁时调用
	/// </summary>
	protected override void OnDestroy() {
		// 订阅场景切换
		SceneManager.sceneLoaded -= OnSceneLoaded;

		// 取消所有事件订阅
		UnsubscribeFromEvents();

		// 重置状态
		ResetStateVariables();

		// Debug
		MPMain.LogInfo(Localization.Get("MPCore", "Destroy"));

		base.OnDestroy();
	}

	#endregion

	#region[RAII函数]

	/// <summary>
	/// 初始化所有管理器
	/// </summary>
	private void InitializeAllManagers() {
		try {
			// 创建Steamworks组件(无状态)
			_MPsteamworks = MPSteamworks.Instance;

			// 创建远程玩家管理器
			_RPManager = RPManager.Instance;
			_RPManager.Initialize(transform);

			// 创建本地信息获取发送管理器
			_LocalPlayer = LocalPlayer.Instance;
			_LocalPlayer.Initialize(MPSteamworks.Instance.UserSteamId, MPConfig.RemotePlayerModel);

			// 初始化网络数据包路由器
			MPPacketRouter.Initialize();

			// 订阅网络事件
			SubscribeToEvents();
			// Debug
			MPMain.LogInfo(Localization.Get("MPCore", "AllManagersInitialized"));
		} catch (Exception e) {
			MPMain.LogError(Localization.Get("MPCore", "ManagerInitializationFailed", e.Message));
		}
	}

	/// <summary>
	/// 初始化网络事件订阅
	/// </summary>
	private void SubscribeToEvents() {
		//// 订阅网络数据接收事件
		//MPEventBusNet.OnReceiveData += ProcessReceiveData;

		// 订阅大厅事件
		MPEventBusNet.OnLobbyEntered += HandleLobbyEntered;
		MPEventBusNet.OnLobbyMemberJoined += HandleLobbyMemberJoined;
		MPEventBusNet.OnLobbyMemberLeft += HandleLobbyMemberLeft;

		// 订阅玩家连接事件
		MPEventBusNet.OnPlayerConnected += HandlePlayerConnected;
		MPEventBusNet.OnPlayerDisconnected += HandlePlayerDisconnected;

		// 订阅游戏事件
		MPEventBusGame.OnPlayerMove += SeedLocalPlayerData;
		MPEventBusGame.OnPlayerDamage += HandlePlayerDamage;
		MPEventBusGame.OnPlayerAddForce += HandlePlayerAddForce;
		MPEventBusGame.OnPlayerDeath += HandlePlayerDeath;
	}

	/// <summary>
	/// 取消所有网络事件订阅
	/// </summary>
	private void UnsubscribeFromEvents() {
		//// 退订网络数据接收事件
		//MPEventBusNet.OnReceiveData -= ProcessReceiveData;

		// 退订大厅事件
		MPEventBusNet.OnLobbyEntered -= HandleLobbyEntered;
		MPEventBusNet.OnLobbyMemberJoined -= HandleLobbyMemberJoined;
		MPEventBusNet.OnLobbyMemberLeft -= HandleLobbyMemberLeft;

		// 退订玩家连接事件
		MPEventBusNet.OnPlayerConnected -= HandlePlayerConnected;
		MPEventBusNet.OnPlayerDisconnected -= HandlePlayerDisconnected;

		// 退订游戏事件
		MPEventBusGame.OnPlayerMove -= SeedLocalPlayerData;
		MPEventBusGame.OnPlayerDamage -= HandlePlayerDamage;
		MPEventBusGame.OnPlayerAddForce -= HandlePlayerAddForce;
		MPEventBusGame.OnPlayerDeath -= HandlePlayerDeath;

	}

	#endregion

	#region[玩家数量同步]
	private void CheckAndRepairPlayers() {

		if (!IsInitialized || !IsInLobby) return;
		if (!_syncTick.TryTick()) return;
		// 在大厅但没有连接
		foreach (var member in _MPsteamworks.Members) {
			if (member.Id == _MPsteamworks.UserSteamId) continue;
			if (!_MPsteamworks._allConnections.ContainsKey(member.Id)) {
				_MPsteamworks.ConnectionController(member.Id, true);
			}
		}
		// 有连接但没有创建对象
		foreach (var (steamId,connection) in _MPsteamworks._allConnections) {
			if (!RPManager.Instance.Players.ContainsKey(steamId)) {
				MPMain.LogWarning(Localization.Get("MPCore", "PlayerDataMissing", steamId));
				// 发送请求玩家创建包
				var writer = GetWriter(_MPsteamworks.UserSteamId, steamId, PacketType.PlayerCreateRequest);
				_MPsteamworks.SendToPeer(steamId, writer);
			}
		}
	}

	#endregion

	#region[场景切换回调]

	/// <summary>
	/// 场景加载完成时调用
	/// </summary>
	private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
		// Debug
		MPMain.LogInfo(Localization.Get("MPCore", "SceneLoadingCompleted", scene.name));

		switch (scene.name) {
			case "Game-Main": {
				// 注册命令和初始化世界数据
				if (CommandConsole.instance != null) {
					RegisterCommands();
					ChangeRPFactoryId();
				} else {
					// Debug
					MPMain.LogError(Localization.Get("MPCore", "CommandConsoleNullAfterSceneLoad"));
				}
				break;
			}
			case "Playground": {
				// 注册命令和初始化世界数据
				if (CommandConsole.instance != null) {
					RegisterCommands();
					ChangeRPFactoryId();
					MultiPlayerStatus.SetField(MPStatus.INIT_MASK, MPStatus.Initialized);
				} else {
					// Debug
					MPMain.LogError(Localization.Get("MPCore", "CommandConsoleNullAfterSceneLoad"));
				}
				break;
			}
			case "Main-Menu":
				ResetStateVariables();
				break;

			default:
				ResetStateVariables();
				break;
		}
	}

	#endregion

	#region[状态设置]

	/// <summary>
	/// 死亡时延迟退出联机模式
	/// </summary>
	private IEnumerator OnDeathSequence() {
		yield return new WaitForSeconds(0.5f);
		ResetStateVariables();
		yield break;
	}

	/// <summary>
	/// 退出联机模式时重置设置
	/// </summary>
	private void ResetStateVariables() {
		MultiPlayerStatus.SetField(MPStatus.INIT_MASK, MPStatus.NotInitialized);
		MultiPlayerStatus.SetField(MPStatus.LOBBY_MASK, MPStatus.NotInLobby);
		_MPsteamworks.DisconnectAll();
		RPManager.Instance.ResetAll();
	}

	/// <summary>
	/// 根据手部皮肤选择玩家模型创建ID
	/// </summary>
	private void ChangeRPFactoryId() {
		// 左右手皮肤相同,尝试映射
		if (CL_CosmeticManager.GetCosmeticInHand(0).cosmeticData.id
			== CL_CosmeticManager.GetCosmeticInHand(1).cosmeticData.id) {
			// 尝试从映射字典中获取对应的玩家模型ID
			if (HandSkinToModelId.TryGetValue(
				CL_CosmeticManager.GetCosmeticInHand(0).cosmeticData.id, out string factoryId)) {
				_LocalPlayer.FactoryId = factoryId;
				return;
			}
		}
		_LocalPlayer.FactoryId = _LocalPlayer.DefaulFactoryId;
	}
	#endregion

	#region[游戏数据收集处理]

	/// <summary>
	/// 发送本地玩家数据
	/// </summary>
	private void SeedLocalPlayerData(PlayerData data) {
		var writer = GetWriter(_MPsteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.PlayerDataUpdate);

		// 进行数据写入
		MPDataSerializer.WriteToNetData(writer, data);
		// 触发Steam数据发送
		// 转为byte[]
		// 使用不可靠+立即发送
		// 广播所有人
		_MPsteamworks.Broadcast(writer, SendType.Unreliable | SendType.NoNagle);
		return;
	}

	/// <summary>
	/// 发送伤害其他玩家数据
	/// </summary>
	private void HandlePlayerDamage(ulong steamId, float amount, string type) {
		var writer = GetWriter(_MPsteamworks.UserSteamId, steamId, PacketType.PlayerDamage);
		writer.Put(amount);
		writer.Put(type);
		_MPsteamworks.SendToPeer(steamId, writer);
	}

	/// <summary>
	/// 发送给予其他玩家冲击力数据
	/// </summary>
	private void HandlePlayerAddForce(ulong steamId, Vector3 force, string source) {
		var writer = GetWriter(_MPsteamworks.UserSteamId, steamId, PacketType.PlayerAddForce);
		writer.Put(force.x);
		writer.Put(force.y);
		writer.Put(force.z);
		writer.Put(source);
		_MPsteamworks.SendToPeer(steamId, writer);
	}

	/// <summary>
	/// 发送玩家死亡信息
	/// </summary>
	private void HandlePlayerDeath(string type) {
		var writer = GetWriter(_MPsteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.PlayerDeath);
		writer.Put(type);
		_MPsteamworks.Broadcast(writer);

		switch (SceneManager.GetActiveScene().name) {
			case "Game-Main": {
				// 断开网络连接
				StartCoroutine(OnDeathSequence());
				break;
			}
			default: {

				break;
			}
		}

	}
	#endregion

	#region[命令注册]

	/// <summary>
	/// 命令注册
	/// </summary>
	private void RegisterCommands() {
		// 将命令注册到 CommandConsole
		CommandConsole.AddCommand("host", Host);
		CommandConsole.AddCommand("join", Join);
		CommandConsole.AddCommand("leave", Leave);
		CommandConsole.AddCommand("getlobbyid", GetLobbyId);
		CommandConsole.AddCommand("allconnections", GetAllConnections);
		CommandConsole.AddCommand("getallplayer", GetAllPlayer);
		CommandConsole.AddCommand("talk", Talk);
		CommandConsole.AddCommand("tpto", TpToPlayer);
		CommandConsole.AddCommand("initialized", Initialized);
		CommandConsole.AddCommand("changemodel", (str) => _LocalPlayer.DefaulFactoryId = str[0]);
		CommandConsole.AddCommand("test", Test.Test.Main, false);
	}

	/// <summary>
	/// 创建大厅
	/// </summary>
	public void Host(string[] args) {
		if (IsInLobby) {
			CommandConsole.LogError(Localization.Get("CommandConsole", "AlreadyInOnlineMode"));
			return;
		}
		if (args.Length < 1) {
			CommandConsole.LogError(Localization.Get("CommandConsole", "HostUsage"));
			return;
		}

		string roomName = args[0];
		int maxPlayers = args.Length >= 2 ? int.Parse(args[1]) : 6;
		// Debug
		MPMain.LogInfo(Localization.Get("MPCore", "CreatingLobby", roomName));

		//设置为正在连接
		MultiPlayerStatus.SetField(MPStatus.LOBBY_MASK, MPStatus.JoiningLobby);

		// 使用协程版本(内部已改为异步)
		_MPsteamworks.CreateRoom(roomName, maxPlayers, (success) => {
			if (success) {
				// 这个触发比加入大厅回调触发慢
				MultiPlayerStatus.SetField(MPStatus.LOBBY_MASK, MPStatus.InLobby);
				MultiPlayerStatus.SetField(MPStatus.INIT_MASK, MPStatus.Initialized);

				switch (SceneManager.GetActiveScene().name) {
					case "Game-Main": {
						WorldLoader.ReloadWithSeed(new string[] { WorldLoader.instance.seed.ToString() });
						break;
					}
					default: {
						break;
					}
				}

			} else {
				MultiPlayerStatus.SetField(MPStatus.LOBBY_MASK, MPStatus.LobbyConnectionError);
				CommandConsole.LogError(Localization.Get("CommandConsole", "CreateLobbyFailed"));
			}
		});
	}

	/// <summary>
	/// 加入大厅
	/// </summary>
	public void Join(string[] args) {
		if (IsInLobby) {
			CommandConsole.LogError(Localization.Get("CommandConsole", "AlreadyInOnlineMode"));
			return;
		}
		if (args.Length < 1) {
			CommandConsole.LogError(Localization.Get("CommandConsole", "JoinUsage"));
			return;
		}

		if (!ulong.TryParse(args[0], out ulong lobbyId)) {
			CommandConsole.LogError(Localization.Get("CommandConsole", "JoinFormatError"));
			return;
		}

		MPMain.LogInfo(Localization.Get("MPCore", "JoiningLobby", lobbyId.ToString()));

		//设置为正在连接
		MultiPlayerStatus.SetField(MPStatus.LOBBY_MASK, MPStatus.JoiningLobby);

		_MPsteamworks.JoinRoom(lobbyId, (success) => {
			if (success) {
				MultiPlayerStatus.SetField(MPStatus.LOBBY_MASK, MPStatus.InLobby);
			} else {
				MultiPlayerStatus.SetField(MPStatus.LOBBY_MASK, MPStatus.LobbyConnectionError);
				CommandConsole.LogError(Localization.Get("CommandConsole", "JoinLobbyFailed"));
			}
		});
	}




	/// <summary>
	/// 离开大厅
	/// </summary>
	public void Leave(string[] args) {
		ResetStateVariables();
		// Debug
		MPMain.LogInfo(Localization.Get("MPCore", "DisconnectedAndCleaned"));
	}

	/// <summary>
	/// 获取大厅ID
	/// </summary>
	public void GetLobbyId(string[] args) {
		if (!IsInLobby) {
			CommandConsole.LogError(Localization.Get("CommandConsole", "NeedToBeOnline"));
			return;
		}
		CommandConsole.Log(Localization.Get(
			"CommandConsole", "LobbyIdOutput", _MPsteamworks.LobbyId.ToString()));
	}

	/// <summary>
	/// 发送信息到他人控制台
	/// </summary>
	public void Talk(string[] args) {
		if (!IsInLobby) {
			CommandConsole.LogError(Localization.Get("CommandConsole", "NeedToBeOnline"));
			return;
		}
		// 将参数数组组合成一个字符串
		string message = string.Join(" ", args);

		var writer = GetWriter(_MPsteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.BroadcastMessage);
		writer.Put(message); // 自动处理长度和编码

		// 发送给所有人
		_MPsteamworks.Broadcast(writer);
	}

	/// <summary>
	/// 向某人TP
	/// </summary>
	public void TpToPlayer(string[] args) {
		if (!IsInLobby) {
			CommandConsole.LogError(Localization.Get("CommandConsole", "NeedToBeOnline"));
			return;
		}
		if (!IsInitialized) {
			CommandConsole.LogError(Localization.Get("CommandConsole", "WorldNotInitialized"));
			return;
		}
		if (ulong.TryParse(args[0], out ulong playerId)) {
			var ids = DictionaryExtensions.FindByKeySuffix(RPManager.Instance.Players, playerId);
			// 未找到对应id
			if (ids.Count == 0) {
				CommandConsole.LogError(Localization.Get("CommandConsole", "TargetIdNotFound"));
				return;
			}
			// 找到多个对应id
			if (ids.Count > 1) {
				string idStr = string.Join("\n", ids);
				CommandConsole.LogError(Localization.Get(
					"CommandConsole", "MultipleMatchingIds", idStr));
				return;
			}
			// 找到对应id,发出传送请求
			var writer = GetWriter(_MPsteamworks.UserSteamId, ids[0], PacketType.PlayerTeleportRequest);
			_MPsteamworks.SendToPeer(ids[0], writer);
		}
	}

	/// <summary>
	/// 调试用,获取所有链接
	/// </summary>
	public void GetAllConnections(string[] args) {
		if (!IsInLobby) {
			CommandConsole.LogError(Localization.Get("CommandConsole", "NeedToBeOnline"));
			return;
		}
		foreach (var (steamid, connection) in _MPsteamworks._outgoingConnections) {
			MPMain.LogInfo(Localization.Get(
				"MPCore", "OutgoingConnectionLog", steamid.ToString(), connection.ToString()));
		}
		foreach (var (steamid, connection) in _MPsteamworks._allConnections) {
			MPMain.LogInfo(Localization.Get(
				"MPCore", "AllConnectionLog", steamid.ToString(), connection.ToString()));
		}
	}

	/// <summary>
	/// 获取全部玩家
	/// </summary>
	public void GetAllPlayer(string[] args) {
		foreach (var friend in _MPsteamworks.Members) {
			CommandConsole.Log(Localization.Get(
				"CommandConsole", "AllPlayer", friend.Name, friend.Id));
		}
	}

	public void Initialized(string[] args) {
		MultiPlayerStatus.SetField(MPStatus.INIT_MASK, MPStatus.Initialized);
	}
	#endregion

	#region[大厅/连接事件触发函数]

	/// <summary>
	/// 处理加入大厅事件
	/// </summary>
	/// <param name="lobby"></param>
	private void HandleLobbyEntered(Lobby lobby) {
		// Debug
		MPMain.LogInfo(Localization.Get("MPCore", "EnteringLobby", lobby.Id.ToString()));

		// 启动协程发送请求初始化数据
		StartCoroutine(InitHandshakeRoutine());
	}

	/// <summary>
	/// 处理大厅成员加入 连接新成员
	/// </summary> 
	private void HandleLobbyMemberJoined(SteamId steamId) {
		if (steamId == _MPsteamworks.UserSteamId) return;
		// Debug
		MPMain.LogInfo(Localization.Get("MPCore", "PlayerJoinedLobby", steamId.ToString()));
	}

	/// <summary>
	/// 处理离开大厅事件
	/// </summary>
	/// <param name="steamId"></param>
	private void HandleLobbyMemberLeft(SteamId steamId) {
		// Debug
		MPMain.LogInfo(Localization.Get("MPCore", "PlayerLeftLobby", steamId.ToString()));
	}

	/// <summary>
	/// 处理事件总线 玩家连接OnPlayerConnected 发送PlayerCreateResponse
	/// </summary>
	private void HandlePlayerConnected(SteamId steamId) {
		// 创建玩家发包
		var writer = GetWriter(_MPsteamworks.UserSteamId, steamId, PacketType.PlayerCreateResponse);
		writer.Put(_LocalPlayer.FactoryId);
		_MPsteamworks.SendToPeer(steamId, writer);
	}

	/// <summary>
	/// 处理事件总线 玩家断连OnPlayerDisconnected
	/// </summary>
	private void HandlePlayerDisconnected(SteamId steamId) {
		// Debug
		MPMain.LogInfo(Localization.Get("MPCore", "PlayerDisconnected", steamId.ToString()));
		RPManager.Instance.PlayerRemove(steamId.Value);
	}

	#endregion

	#region [网络数据处理]

	/// <summary>
	/// 客户端发送WorldInitRequest: 协程请求初始化数据
	/// </summary>
	public IEnumerator InitHandshakeRoutine() {
		yield return new WaitForSeconds(1.0f);
		// 在大厅并且未加载
		while (IsInLobby && !IsInitialized) {
			MPMain.LogInfo(Localization.Get("MPCore", "RequestedInitData"));
			var writer = GetWriter(_MPsteamworks.UserSteamId, _MPsteamworks.HostSteamId, PacketType.WorldInitRequest);
			_MPsteamworks.SendToHost(writer);
			yield return new WaitForSeconds(4.0f);
		}
	}

	///// <summary>
	///// 主机接收WorldInitRequest: 请求初始化数据
	///// 发送WorldInitData: 初始化数据给新玩家
	///// </summary>
	//private void ProcessWorldInitRequest(ulong steamId) {
	//	// 发送世界种子
	//	var writer = GetWriter(Steamworks.UserSteamId, steamId, PacketType.WorldInitData);
	//	writer.Put(WorldLoader.instance.seed);
	//	Steamworks.SendToPeer(steamId, writer);

	//	// 可以添加其他初始化数据,如游戏状态、物品状态等

	//	// Debug
	//	MPMain.LogInfo(Localization.Get("MPCore", "SentInitData"));
	//}

	/// <summary>
	/// 客户端接收WorldInitData: 新加入玩家,加载世界种子
	/// </summary>
	/// <param name="seed"></param>
	//private void ProcessWorldInit(ArraySegment<byte> payload) {
	//	var reader = GetReader(payload);
	//	// 获取种子
	//	int seed = reader.GetInt();
	//	// Debug
	//	MPMain.LogInfo(Localization.Get("MPCore", "LoadingWorld", seed.ToString()));
	//	// 种子相同默认为已经联机过,只不过断开了
	//	if (seed != WorldLoader.instance.seed)
	//		WorldLoader.ReloadWithSeed(new string[] { seed.ToString() });
	//	MultiPlayerStatus.SetField(MPStatus.INIT_MASK, MPStatus.Initialized);
	//}

	///// <summary>
	///// 主机/客户端接收PlayerDataUpdate: 处理玩家数据更新
	///// </summary>
	//private void ProcessPlayerDataUpdate(ArraySegment<byte> payload) {
	//	var reader = GetReader(payload);

	//	// 如果是从转发给自己的,忽略
	//	var playerData = MPDataSerializer.ReadFromNetData(reader);
	//	var playId = playerData.playId;
	//	if (playId == Steamworks.UserSteamId) {
	//		return;
	//	}
	//	RPManager.ProcessPlayerData(playId, playerData);
	//}

	///// <summary>
	///// 主机/客户端接收BroadcastMessage: 处理玩家标签更新
	///// </summary>
	//private void ProcessPlayerTagUpdate(ulong senderId, ArraySegment<byte> payload) {
	//	var reader = GetReader(payload);

	//	string msg = reader.GetString();    // 读取消息
	//	string playerName = new Friend(senderId).Name;
	//	CommandConsole.Log($"{playerName}: {msg}");
	//	RPManager.Players[senderId].UpdateNameTag(msg);
	//}

	///// <summary>
	///// 主机/客户端接收PlayerDamage: 受到伤害
	///// </summary>
	//private void ProcessPlayerDamage(ulong senderId, ArraySegment<byte> payload) {
	//	var reader = GetReader(payload);
	//	float amount = reader.GetFloat();
	//	string type = reader.GetString();
	//	var baseDamage = amount * MPConfig.AllPassive;
	//	switch (type) {
	//		case "Hammer":
	//			ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.HammerPassive, type);
	//			break;
	//		case "rebar":
	//			ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.RebarPassive, type);
	//			break;
	//		case "returnrebar":
	//			ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.ReturnRebarPassive, type);
	//			break;
	//		case "rebarexplosion":
	//			ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.RebarExplosionPassive, type);
	//			break;
	//		case "explosion":
	//			ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.ExplosionPassive, type);
	//			break;
	//		case "piton":
	//			ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.PitonPassive, type);
	//			break;
	//		case "flare":
	//			ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.FlarePassive, type);
	//			break;
	//		case "ice":
	//			ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.IcePassive, type);
	//			break;
	//		default:
	//			ENT_Player.GetPlayer().Damage(baseDamage * MPConfig.OtherPassive, type);
	//			break;
	//	}
	//}

	///// <summary>
	///// 主机/客户端接收PlayerAddForce: 受到冲击力
	///// </summary>
	//private void ProcessPlayerAddForce(ulong senderId, ArraySegment<byte> payload) {
	//	var reader = GetReader(payload);
	//	Vector3 force = new Vector3 {
	//		x = reader.GetFloat(),
	//		y = reader.GetFloat(),
	//		z = reader.GetFloat(),
	//	};
	//	string source = reader.GetString();
	//	ENT_Player.GetPlayer().AddForce(force, source);
	//}

	///// <summary>
	///// 主机/客户端接收PlayerDeath: 玩家死亡
	///// </summary>
	//private void ProcessPlayerDeath(ulong senderId, ArraySegment<byte> payload) {
	//	var reader = GetReader(payload);
	//	string type = reader.GetString();
	//	string playerName = new Friend(senderId).Name;
	//	CommandConsole.Log(Localization.Get("CommandConsole", "PlayerDeath", playerName, type));

	//}

	///// <summary>
	///// 主机/客户端接收PlayerCreateRequest<br/>
	///// 发送PlayerCreateResponse: 携带远程玩家工厂ID,让请求方创建远程玩家对象
	///// </summary>
	//private void ProcessPlayerCreateRequest(ulong senderId) { 
	//	var writer = GetWriter(Steamworks.UserSteamId, senderId, PacketType.PlayerCreateResponse);
	//	writer.Put(LPManager.FactoryId);
	//	Steamworks.SendToPeer(senderId, writer);
	//}

	///// <summary>
	///// 主机/客户端接收PlayerCreateResponse: 创建玩家对象
	///// </summary>
	//private void ProcessPlayerCreateResponse(ulong senderId, ArraySegment<byte> payload) {
	//	var reader = GetReader(payload);
	//	string factoryId = reader.GetString();
	//	RPManager.PlayerCreate(senderId, factoryId);
	//}

	///// <summary>
	///// 主机/客户端接收PlayerTeleport<br/>
	///// 发送RespondPlayerTeleport: 有Mess环境则携带Mess数据
	///// </summary>
	///// <param name="senderId">发送方ID</param>
	//private void ProcessPlayerTeleport(ulong senderId, ArraySegment<byte> payload) {
	//	// 获取数据
	//	var positionData = ENT_Player.GetPlayer().transform.position;
	//	var writer = GetWriter(Steamworks.UserSteamId, senderId, PacketType.PlayerTeleportRespond);
	//	writer.Put(positionData.x);
	//	writer.Put(positionData.y);
	//	writer.Put(positionData.z);

	//	if (DEN_DeathFloor.instance == null) {
	//		writer.Put(false);
	//	} else {
	//		var deathFloorData = DEN_DeathFloor.instance.GetSaveData();
	//		writer.Put(true);
	//		writer.Put(deathFloorData.relativeHeight);
	//		writer.Put(deathFloorData.active);
	//		writer.Put(deathFloorData.speed);
	//		writer.Put(deathFloorData.speedMult);
	//	}
	//	Steamworks.SendToPeer(senderId, writer);
	//}

	///// <summary>
	///// 主机/客户端接收RespondPlayerTeleport: 传送并同步Mess数据
	///// </summary>
	///// <param name="senderId">发送ID</param>
	//private void ProcessRespondPlayerTeleport(ulong senderId, ArraySegment<byte> payload) {
	//	var reader = GetReader(payload);
	//	var posX = reader.GetFloat();
	//	var posY = reader.GetFloat();
	//	var posZ = reader.GetFloat();
	//	if (reader.GetBool()) {
	//		var deathFloorData = new DEN_DeathFloor.SaveData {
	//			relativeHeight = reader.GetFloat(),
	//			active = reader.GetBool(),
	//			speed = reader.GetFloat(),
	//			speedMult = reader.GetFloat(),
	//		};

	//		// 关闭可击杀效果
	//		DEN_DeathFloor.instance.SetCanKill(new string[] { "false" });
	//		// 重设计数器,期间位移视为传送
	//		LPManager.TriggerTeleport();
	//		ENT_Player.GetPlayer().Teleport(new Vector3(posX, posY, posZ));
	//		DEN_DeathFloor.instance.LoadDataFromSave(deathFloorData);
	//		DEN_DeathFloor.instance.SetCanKill(new string[] { "true" });
	//	} else {
	//		// 重设计数器,期间位移视为传送
	//		LPManager.TriggerTeleport();
	//		ENT_Player.GetPlayer().Teleport(new Vector3(posX, posY, posZ));
	//	}

	//}

	///// <summary>
	///// 处理网络接收数据
	///// </summary>
	//private void ProcessReceiveData(ulong connectionId, ArraySegment<byte> data) {
	//	if (data.Array == null || data.Count < 20) return;
	//	// 基本验证:确保数据足够读取一个整数(数据包类型)

	//	// 直接解析头部
	//	ReadOnlySpan<byte> span = data;
	//	// 发送方ID
	//	ulong senderId = ReadUInt64LittleEndian(span);
	//	// 接收方ID
	//	ulong targetId = ReadUInt64LittleEndian(span.Slice(8));

	//	// 验证:如果发件人 ID 和物理连接 ID 对不上,可能是伪造包
	//	if (senderId != connectionId) return;

	//	// 转发:目标不是我,也不是广播,也不是特殊判断ID
	//	if (targetId != Steamworks.UserSteamId
	//		&& targetId != Steamworks.BroadcastId
	//		&& targetId != Steamworks.SpecialId) {
	//		ProcessForwardToPeer(targetId, data);
	//		return; // 结束
	//	}

	//	// 广播:如果是广播,且不是我发出的
	//	if (targetId == Steamworks.BroadcastId
	//		&& senderId != Steamworks.UserSteamId) {
	//		ProcessBroadcastExcept(senderId, data);
	//		// 继续往下走,因为主机也要处理广播包
	//	}

	//	// 包类型
	//	PacketType packetType = (PacketType)ReadInt32LittleEndian(span.Slice(16));
	//	// 包具体数据
	//	var payload = data.Slice(20);

	//	switch (packetType) {
	//		// 接收: 世界初始化请求
	//		case PacketType.WorldInitRequest: {
	//			ProcessWorldInitRequest(senderId);
	//			break;
	//		}
	//		// 接收: 种子加载
	//		case PacketType.WorldInitData: {
	//			ProcessWorldInit(payload);
	//			break;
	//		}
	//		// 接收: 玩家数据更新
	//		case PacketType.PlayerDataUpdate: {
	//			ProcessPlayerDataUpdate(payload);
	//			break;
	//		}
	//		// 接收: 世界状态同步
	//		case PacketType.WorldStateSync: {
	//			break;
	//		}
	//		// 接收: 接收文字信息
	//		case PacketType.BroadcastMessage: {
	//			ProcessPlayerTagUpdate(senderId, payload);
	//			break;
	//		}
	//		// 接收: 受到伤害
	//		case PacketType.PlayerDamage: {
	//			ProcessPlayerDamage(senderId, payload);
	//			break;
	//		}
	//		// 接收: 受到冲击力
	//		case PacketType.PlayerAddForce: {
	//			ProcessPlayerAddForce(senderId, payload);
	//			break;
	//		}
	//		// 接收: 玩家死亡及其死亡原因
	//		case PacketType.PlayerDeath: {
	//			ProcessPlayerDeath(senderId, payload);
	//			break;
	//		}
	//		// 接收: 创建玩家请求
	//		case PacketType.PlayerCreateRequest: {
	//			ProcessPlayerCreateRequest(senderId);
	//			break;
	//		}
	//		// 接收: 创建玩家响应 (包含玩家工厂ID)
	//		case PacketType.PlayerCreateResponse: {
	//			ProcessPlayerCreateResponse(senderId, payload);
	//			break;
	//		}

	//		// 接收: 传送请求
	//		case PacketType.PlayerTeleportRequest: {
	//			ProcessPlayerTeleport(senderId, payload);
	//			break;
	//		}
	//		// 接收: 传送响应
	//		case PacketType.PlayerTeleportRespond: {
	//			ProcessRespondPlayerTeleport(senderId, payload);
	//			break;
	//		}
	//	}
	//}
	#endregion

	#region[网络发送工具类]
	///// <summary>
	///// 转发网络数据包到指定的客户端
	///// </summary>
	//private void ProcessForwardToPeer(ulong targetId, ArraySegment<byte> data) {
	//	// 直接从 segment 获取偏移和长度
	//	int offset = data.Offset;
	//	int count = data.Count;
	//	// 解析类型
	//	PacketType type = (PacketType)BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16, 4));
	//	SendType st = (type == PacketType.PlayerDataUpdate)
	//		? SendType.Unreliable : SendType.Reliable;

	//	Steamworks.SendToPeer(targetId, data.Array, offset, count, st);
	//}

	///// <summary>
	///// 广播数据包到所有客户端
	///// </summary>
	//public void ProcessBroadcast(ArraySegment<byte> data) {
	//	// 直接从 segment 获取偏移和长度
	//	int offset = data.Offset;
	//	int count = data.Count;

	//	// 解析类型
	//	PacketType type = (PacketType)BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16, 4));

	//	SendType st = (type == PacketType.PlayerDataUpdate)
	//		? SendType.Unreliable : SendType.Reliable;

	//	// 将全套参数传给底层
	//	Steamworks.Broadcast(data.Array, offset, count, st);
	//}

	///// <summary>
	///// 广播数据包到所有客户端 (除了发送者)
	///// </summary>
	///// <param name="senderId">发送方ID</param>
	//public void ProcessBroadcastExcept(ulong senderId, ArraySegment<byte> data) {
	//	// 直接从 segment 获取偏移和长度
	//	int offset = data.Offset;
	//	int count = data.Count;

	//	// 解析类型
	//	PacketType type = (PacketType)BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16, 4));

	//	SendType st = (type == PacketType.PlayerDataUpdate)
	//		? SendType.Unreliable : SendType.Reliable;

	//	// 将全套参数传给底层
	//	Steamworks.BroadcastExcept(senderId, data.Array, offset, count, st);
	//}
	#endregion

}
