using TMPro;
using UnityEngine;
using WKMPMod.Component;
using WKMPMod.Core;
using WKMPMod.MK_Component;
using WKMPMod.Util;
using Object = UnityEngine.Object;

namespace WKMPMod.RemotePlayer;

public abstract class BaseRemoteFactory {
	// 由 FactoryManager 注入
	public string PrefabName { get; set; }
	public string FactoryId { get; set; }

	// 缓存预制体
	private GameObject _cachedPrefab;

	public GameObject Create(string bundlePath) { 
		if (_cachedPrefab == null) {
			_cachedPrefab = LoadAndPrepare(bundlePath);
			if (_cachedPrefab == null) {
				MPMain.LogError(Localization.Get("RemotePlayerFactory", "PrefabNotLoaded", PrefabName));
				return null;
			}
		}
		return GameObject.Instantiate(_cachedPrefab);
	}

	// 加载并处理预制体
	public GameObject LoadAndPrepare(string path) {
		var bundle = AssetBundle.LoadFromFile(path);

		if (bundle == null) {
			MPMain.LogError(Localization.Get("RemotePlayerFactory", "UnableToLoadResources"));
			return null;
		}

		// Debug 函数输出所有资源
		ListAllAssetsInBundle(bundle);

		var raw = bundle.LoadAsset<GameObject>(PrefabName);

		// Shader修复 和 组件替换
		ProcessPrefabMarkers(raw);	// 替换标记组件
		FixShaders(raw);			// 通用 Shader 修复
		AddFactoryId(raw);			// 挂载身份证
		OnPrepare(raw, bundle);		// 子类特化处理

		bundle.Unload(false); // 卸载资源,保留预制体

		return raw;
	}

	#region[接口]

	/// <summary>
	/// 子类特化处理
	/// </summary>
	protected abstract void OnPrepare(GameObject prefab, AssetBundle bundle);
	/// <summary>
	/// 资源清理
	/// </summary>
	public abstract void Cleanup(GameObject instance);

	#endregion

	#region[shader/材质丢失修复]

	/// <summary>
	/// 修复 Shader 丢失
	/// </summary>

	private void FixShaders(GameObject prefab) {
		foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true)) {
			// 如果是 TMP 的 3D 渲染器,使用TMP特化逻辑处理
			if (renderer.GetComponent<TMP_Text>() != null) continue;

			foreach (var mat in renderer.sharedMaterials) {
				if (mat == null) continue;
				MPMain.LogInfo(Localization.Get("RemotePlayerFactory", "MaterialShaderInfo", mat.name, mat.shader.name));
				// 强制链接到游戏的 Shader
				var internalShader = Shader.Find(mat.shader.name);
				if (internalShader != null)
					mat.shader = internalShader;
				else {
					MPMain.LogError(Localization.Get("RemotePlayerFactory", "ShaderNotFoundOnRenderer", mat.shader.name, renderer.name));
				}
			}
		}
	}

	#endregion

	#region[将标记组件替换为真实组件]

	/// <summary>
	/// 遍历对象及其子对象进行检测
	/// </summary>
	public void ProcessPrefabMarkers(GameObject prefab) {
		// 批量处理 MK_RemoteEntity
		var remoteEntities = prefab.GetComponentsInChildren<MK_RemoteEntity>(true);
		foreach (var mk in remoteEntities) {
			MapMarkersToRemoteEntity(mk.gameObject, mk);
		}

		// 批量处理 MK_ObjectTagger
		var taggers = prefab.GetComponentsInChildren<MK_ObjectTagger>(true);
		foreach (var mk in taggers) {
			MapMarkersToObjectTagger(mk.gameObject, mk);
		}

		// 批量处理 MK_CL_Handhold
		var handholds = prefab.GetComponentsInChildren<MK_CL_Handhold>(true);
		foreach (var mk in handholds) {
			MapMarkersToCL_Handhold(mk.gameObject, mk);
		}

		// 批量处理 LookAt
		var lookAts = prefab.GetComponentsInChildren<LookAt>(true);
		foreach (var la in lookAts) {
			SetLookAt(la.gameObject, la);
		}
	}

	private void MapMarkersToRemoteEntity(GameObject go, MK_RemoteEntity mk) {
		var component = go.AddComponent<RemoteEntity>();
		if (component != null) {
			component.AllActive = MPConfig.AllActive;
			component.HammerActive = MPConfig.HammerActive;
			component.RebarActive = MPConfig.RebarActive;
			component.ReturnRebarActive = MPConfig.ReturnRebarActive;
			component.RebarExplosionActive = MPConfig.RebarExplosionActive;
			component.ExplosionActive = MPConfig.ExplosionActive;
			component.PitonActive = MPConfig.PitonActive;
			component.FlareActive = MPConfig.FlareActive;
			component.IceActive = MPConfig.IceActive;
			component.OtherActive = MPConfig.OtherActive;
		} else {
			MPMain.LogError(Localization.Get("RemotePlayerFactory", "RemoteEntityAddFailed"));
		}
		Object.DestroyImmediate(mk);
	}

	private void MapMarkersToObjectTagger(GameObject go, MK_ObjectTagger mk) {
		var component = go.GetComponent<ObjectTagger>() ?? go.AddComponent<ObjectTagger>();
		if (component != null) {
			// 使用for循环添加标签
			foreach (var t in mk.tags) {
				if (!component.tags.Contains(t)) {
					component.tags.Add(t);
				}
			}
		} else {
			MPMain.LogError(Localization.Get("RemotePlayerFactory", "ObjectTaggerAddFailed"));
		}
		Object.DestroyImmediate(mk);
	}

	private void MapMarkersToCL_Handhold(GameObject go, MK_CL_Handhold mk) {
		var component = go.AddComponent<CL_Handhold>();
		if (component != null) {
			component.activeEvent = mk.activeEvent;
			component.stopEvent = mk.stopEvent;
			component.handholdRenderer = mk.handholdRenderer ?? go.GetComponent<Renderer>();

		} else {
			MPMain.LogError(Localization.Get("RemotePlayerFactory", "CL_HandholdAddFailed"));
		}
		Object.DestroyImmediate(mk);
	}

	private void SetLookAt(GameObject go, LookAt mk) {
		mk.userScale = MPConfig.NameTagScale;
	}

	#endregion

	#region[添加工厂标签(用于销毁定位工厂)]

	private void AddFactoryId(GameObject prefab) {
		var factoryId = prefab.AddComponent<ObjectIdentity>();
		factoryId.name = FactoryId;
	}

	#endregion

	#region[Debug]

	/// <summary>
	/// 遍历并输出 Bundle 内所有资源的完整路径和类型
	/// </summary>
	public static void ListAllAssetsInBundle(AssetBundle bundle) {
		MPMain.LogInfo($"--- 开始输出 AssetBundle 内容清单: [{bundle.name}] ---");

		// 获取包内所有资源的路径名称
		string[] assetNames = bundle.GetAllAssetNames();

		if (assetNames.Length == 0) {
			MPMain.LogWarning("警告：该 AssetBundle 是空的！");
			return;
		}

		foreach (string name in assetNames) {
			// 尝试加载资源以获取其实际类型(仅用于 Debug)
			Object asset = bundle.LoadAsset(name);
			string typeName = asset != null ? asset.GetType().Name : "Unknown Type";

			MPMain.LogInfo($"[资源清单] 路径: {name} | 类型: {typeName}");

			// 如果是文件夹或特殊的资源,有时需要额外注意
		}

		MPMain.LogInfo($"--- 清单输出完毕,共计 {assetNames.Length} 个资源 ---");
	}

	#endregion
}