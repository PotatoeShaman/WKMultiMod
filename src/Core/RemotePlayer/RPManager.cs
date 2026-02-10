using Steamworks;
using System.Collections.Generic;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.Util;
using Object = UnityEngine.Object;

namespace WKMPMod.RemotePlayer;

// 生命周期为全局
public class RPManager : Singleton<RPManager> {

	// Debug日志输出间隔
	private TickTimer _debugTick = new TickTimer(5f);
	// 存储所有远程对象
	internal Dictionary<ulong, RPContainer> Players = new Dictionary<ulong, RPContainer>();
	// 根对象引用
	private Transform _remotePlayersRoot;

	private RPManager() {
		_ = RPFactoryManager.Instance;
	}

	public void Initialize(Transform RootTransform) {
		_remotePlayersRoot = RootTransform;
	}

	/// <summary>
	/// 清除全部玩家
	/// </summary>
	public void ResetAll() {
		foreach (var container in Players.Values) {
			RPFactoryManager.Instance.Cleanup(container.PlayerObject);

			container.Destroy();
		}
		Players.Clear();
	}

	#region[创建/销毁玩家]
	/// <summary>
	/// 根据Id创建玩家
	/// </summary>
	public RPContainer PlayerCreate(ulong playId,string prefab) {
		if (Players.TryGetValue(playId, out var existing)) 
			return existing;

		var container = new RPContainer(playId);

		// 从工厂直接获取实例
		GameObject instance = RPFactoryManager.Instance.Create(prefab);

		if (instance == null) {
			MPMain.LogError(Localization.Get("RPManager", "FactoryCreateObjectFailed"));
			return null;
		}

		container.Initialize(instance, _remotePlayersRoot);
		Players[playId] = container;
		return container;
	}

	/// <summary>
	/// 清除特定玩家
	/// </summary>
	public void PlayerRemove(ulong playId) {
		if (Players.TryGetValue(playId, out var container)) {

			// 工厂清理
			RPFactoryManager.Instance.Cleanup(container.PlayerObject);

			// 容器清理引用
			container.Destroy();

			Players.Remove(playId);
		}
	}
	#endregion

	#region[处理消息]

	/// <summary>
	/// 处理玩家数据
	/// </summary>
	public void ProcessPlayerData(ulong playerId, PlayerData playerData) {

		// 以后加上时间戳处理
		if (Players.TryGetValue(playerId, out var RPcontainer)) {
			RPcontainer.UpdatePlayerData(playerData);
			return;
		} else if (_debugTick.TryTick()) {
			MPMain.LogError(Localization.Get(
				"RPManager", "RemotePlayerObjectNotFound", playerId.ToString()));
			return;
		}
		return;
	}

	/// <summary>
	/// 处理玩家数据
	/// </summary>
	public void ProcessPlayerTag(ulong playerId, string massage) {

		// 以后加上时间戳处理
		if (Players.TryGetValue(playerId, out var RPcontainer)) {
			RPcontainer.UpdateNameTag(massage);
			return;
		} else {
			MPMain.LogError(Localization.Get(
				"RPManager", "RemotePlayerObjectNotFound", playerId.ToString()));
			return;
		}
	}

	#endregion
}