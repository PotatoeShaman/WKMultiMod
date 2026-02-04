using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using WKMPMod.Component;
using WKMPMod.Core;
using WKMPMod.Util;
using Object = UnityEngine.Object;

namespace WKMPMod.RemotePlayer;

public class RPFactoryManager {
	private static readonly Lazy<RPFactoryManager> _instance =
		new Lazy<RPFactoryManager>(() => new RPFactoryManager());

	public static RPFactoryManager Instance => _instance.Value;
	private readonly object _lock = new object();
	private bool _initialized = false;

	// 静态字典 在FactoryManager实例化之前就可以注册
	private static Dictionary<string, FactoryRegistration> _factories = new Dictionary<string, FactoryRegistration>();

	#region[生命周期函数]

	private RPFactoryManager() { }
	public void Initialize() {
		lock (_lock) {
			if (_initialized) {
				MPMain.LogWarning(Localization.Get("RPFactoryManager", "AlreadyInitialized"));
				return;
			}

			RegisterDefaultFactories();
			_initialized = true;

			MPMain.LogInfo(Localization.Get("RPFactoryManager", "Initialized"));
		}
	}

	#endregion

	#region[静态注册接口]

	/// <summary>
	/// 静态注册方法
	/// </summary>
	/// <param name="factoryId">工厂类字典ID</param>
	/// <param name="factory">工厂类实例</param>
	/// <param name="prefabName">预制体名称</param>
	/// <param name="bundlePath">AssetBundle完整路径</param>
	public static void RegisterFactory(string factoryId, BaseRemoteFactory factory, string prefabName, string bundlePath) {
		if (_factories.ContainsKey(factoryId)) {
			MPMain.LogWarning(Localization.Get("RPFactoryManager", "FactoryAlreadyRegistered", factoryId));
			return;
		}

		factory.PrefabName = prefabName;
		factory.FactoryId = factoryId;

		_factories.Add(factoryId, new FactoryRegistration {
			Factory = factory,
			BundlePath = bundlePath
		});

		MPMain.LogInfo(Localization.Get("RPFactoryManager", "FactoryRegistered", factoryId));
	}

	#endregion

	/// <summary>
	/// 创建对象
	/// </summary>
	public GameObject Create(string factoryId) {
		if (_factories.TryGetValue(factoryId, out var registration)) {
			return registration.Factory.Create(registration.BundlePath);
		}

		MPMain.LogError(Localization.Get("RPFactoryManager", "FactoryNotFound", factoryId));
		// 生成默认模型
		if (_factories.TryGetValue("default", out var defaultRegistration)) {
			return registration.Factory.Create(defaultRegistration.BundlePath);
		}

		MPMain.LogError(Localization.Get("RPFactoryManager", "FactoryNotFound", "default"));
		return null;
	}

	/// <summary>
	/// 清理对象
	/// </summary>
	public void Cleanup(GameObject instance) {
		if (instance == null) return;

		var identity = instance.GetComponent<ObjectIdentity>();
		if (identity == null || string.IsNullOrEmpty(identity.name)) {
			MPMain.LogError(Localization.Get("RPFactoryManager", "CannotDetermineFactory"));
			Object.Destroy(instance);
			return;
		}

		if (_factories.TryGetValue(identity.name, out var registration)) {
			registration.Factory.Cleanup(instance);
		} else {
			MPMain.LogError(Localization.Get("RPFactoryManager", "FactoryNotFoundCleanup", identity.name));
			Object.Destroy(instance);
		}
	}

	/// <summary>
	/// 清理工厂字典数据
	/// </summary>
	public void Reset() {
		lock (_lock) {
			_factories.Clear();
			_initialized = false;

			MPMain.LogInfo(Localization.Get("RPFactoryManager", "Reset"));
		}
	}

	/// <summary>
	/// 注册默认工厂
	/// </summary>
	private void RegisterDefaultFactories() {
		RegisterFactory(
			"slugcat",
			new SlugcatFactory(),
			"SlugcatPlayerPrefab",
			Path.Combine(MPMain.path, "player_prefab")
		);
		RegisterFactory(
			"default",
			new SlugcatFactory(),
			"CapsulePlayerPrefab",
			Path.Combine(MPMain.path, "player_prefab")
		);
	}


	private class FactoryRegistration {
		public BaseRemoteFactory Factory { get; set; }
		public string BundlePath { get; set; }
	}

	#region[Debug]

	public void ListAllFactory() {
		foreach (var (factoryId, factory) in _factories) {
			MPMain.LogWarning(Localization.Get("RPFactoryManager", "DebugFactoryInfo",
				factoryId, factory.Factory.PrefabName, factory.BundlePath));
		}
	}

	#endregion
}