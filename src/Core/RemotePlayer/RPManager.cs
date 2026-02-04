using System.Collections.Generic;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.Util;
using Object = UnityEngine.Object;

namespace WKMPMod.RemotePlayer;

// 生命周期为全局
public class RPManager : MonoBehaviour {

	// Debug日志输出间隔
	private TickTimer _debugTick = new TickTimer(5f);
	// 存储所有远程对象
	internal Dictionary<ulong, RPContainer> Players = new Dictionary<ulong, RPContainer>();

	void Awake() {
		// 确保根对象存在
		EnsureRootObject();
		RPFactoryManager.Instance.Initialize();
	}

	void OnDestroy() {
		ResetAll();
		RPFactoryManager.Instance.Reset();
	}

	/// <summary>
	/// 清除全部玩家
	/// </summary>
	public void ResetAll() {
		foreach (var container in Players.Values) {
			container.Destroy();
		}
		Players.Clear();
	}

	/// <summary>
	/// 确保根对象存在
	/// </summary>
	private void EnsureRootObject() {
		// 直接在MultiplayerCore下查找或创建
		var coreTransform = transform.parent; // MultiplayerCore
		var rootName = "RemotePlayers";

		if (coreTransform.Find(rootName) == null) {
			var rootObj = new GameObject(rootName);
			rootObj.transform.SetParent(coreTransform, false);
		}
	}

	/// <summary>
	/// 获取远程玩家根Transform
	/// </summary>
	private Transform GetRemotePlayersRoot() {
		var coreTransform = transform.parent;
		var rootName = "RemotePlayers";

		var root = coreTransform.Find(rootName);
		if (root == null) {
			// 如果找不到,创建一个(应该不会发生,因为EnsureRootObject已调用)
			root = new GameObject(rootName).transform;
			root.SetParent(coreTransform, false);
		}

		return root;
	}

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

		container.Initialize(instance, GetRemotePlayersRoot());
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

	/// <summary>
	/// 处理玩家数据
	/// </summary>
	public void ProcessPlayerData(ulong playId, PlayerData playerData) {

		// 以后加上时间戳处理
		if (Players.TryGetValue(playId, out var RPcontainer)) {
			RPcontainer.UpdatePlayerData(playerData);
			return;
		} else if (_debugTick.TryTick()) {
			MPMain.LogError(Localization.Get(
				"RPManger", "RemotePlayerObjectNotFound", playId.ToString()));
			return;
		}
		return;
	}
}