using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using WKMPMod.Component;
using WKMPMod.Core;
using WKMPMod.Shared.MK_Component;
using WKMPMod.Util;
using Object = UnityEngine.Object;

namespace WKMPMod.RemoteManager;

public static class RemotePlayerFactory {
	private static GameObject _slugcatPrefab;
	// 蛞蝓猫文件地址
	private const string SLUGCAT_FILE_NAME = "slugcat_prefab";
	// 蛞蝓猫预制体名称
	private const string SLUGCAT_PREFAB_NAME = "SlugcatPrefab";
	// 透视字体材质
	private const string TMP_DISTANCE_FIELD_OVERLAY_MAT = 
		"assets/projects/slugcat/materials/textmeshpro_distance field overlay.mat";
	// 游戏字体贴图
	private const string GAME_TMP_FONT_ASSET = "Ticketing SDF";
	// 获取完整路径
	private static string BundlePath => Path.Combine(MPMain.path, SLUGCAT_FILE_NAME);

	// 加载并处理预制体
	public static void LoadAndPrepare(string path) {
		if (_slugcatPrefab != null) return;

		var bundle = AssetBundle.LoadFromFile(path);

		if (bundle == null) {
			MPMain.LogError(Localization.Get("RemotePlayerFactory", "UnableToLoadResources"));
			return;
		}

		// Debug 函数输出所有资源
		//ListAllAssetsInBundle(bundle);

		var rawPrefab = bundle.LoadAsset<GameObject>(SLUGCAT_PREFAB_NAME);

		// Shader修复 和 组件替换
		_slugcatPrefab = PreparePrefab(rawPrefab, bundle);

		bundle.Unload(false); // 卸载镜像,保留资源
	}

	/// <summary>
	/// 工厂接口
	/// </summary>
	public static GameObject CreateInstance() {
		if (_slugcatPrefab == null) {
			LoadAndPrepare(BundlePath);
			if (_slugcatPrefab == null) {
				return null;
			}
		}
		return Object.Instantiate(_slugcatPrefab);
	}

	/// <summary>
	/// 处理预制体
	/// </summary>
	private static GameObject PreparePrefab(GameObject prefab, AssetBundle bundle) {
		FixShaders(prefab); // 修复材质
		FixTMPComponent(prefab, bundle); // 修复字体
		ProcessPrefabMarkers(prefab); // 替换组件

		return prefab;
	}

	#region[shader/材质丢失修复]
	/// <summary>
	/// 修复 Shader 丢失
	/// </summary>

	private static void FixShaders(GameObject prefab) {
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

	/// <summary>
	/// 修复TMP字体和材质
	/// </summary>
	private static void FixTMPComponent(GameObject prefab,AssetBundle bundle) {
		// 特化处理 TextMeshPro
		foreach (var tmpText in prefab.GetComponentsInChildren<TMP_Text>(true)) {
			MPMain.LogInfo(Localization.Get("RemotePlayerFactory", "SpecializingTMPComponent", tmpText.name));

			// 游戏内原生字体
			TMP_FontAsset gameFont = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
							 .FirstOrDefault(f => f.name == GAME_TMP_FONT_ASSET);
			if (gameFont == null) {
				MPMain.LogError(Localization.Get("RemotePlayerFactory", "FontAssetNotFound", GAME_TMP_FONT_ASSET));
				continue;
			}
			// 赋值组件字体
			tmpText.font = gameFont;

			// 透视字体材质
			Material bundleMat = bundle.LoadAsset<Material>(TMP_DISTANCE_FIELD_OVERLAY_MAT);
			// 实例材质副本
			Material instanceMat = tmpText.fontMaterial;
			if (instanceMat != null && bundleMat != null) {
				// Overlay Shader 赋给实例副本
				instanceMat.shader = bundleMat.shader;

				MPMain.LogInfo(Localization.Get("RemotePlayerFactory", "ImplementOverlayViaShader"));
			} else {
				MPMain.LogError(Localization.Get("RemotePlayerFactory", "UnableToLoadMaterial",TMP_DISTANCE_FIELD_OVERLAY_MAT));
			}
		}
	}

	#endregion

	#region[将标记组件替换为真实组件]

	/// <summary>
	/// 遍历对象及其子对象进行检测
	/// </summary>
	public static void ProcessPrefabMarkers(GameObject prefab) {
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

	private static void MapMarkersToRemoteEntity(GameObject go, MK_RemoteEntity mk) {
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

	private static void MapMarkersToObjectTagger(GameObject go, MK_ObjectTagger mk) {
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

	private static void MapMarkersToCL_Handhold(GameObject go, MK_CL_Handhold mk) {
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

	private static void SetLookAt(GameObject go, LookAt mk) {
		mk.userScale = MPConfig.NameTagScale;
	}

	#endregion

	#region[Debug]

	/// <summary>
	/// 遍历并输出 Bundle 内所有资源的完整路径和类型
	/// </summary>
	private static void ListAllAssetsInBundle(AssetBundle bundle) {
		MPMain.LogInfo($"--- 开始输出 AssetBundle 内容清单: [{bundle.name}] ---");

		// 获取包内所有资源的路径名称
		string[] assetNames = bundle.GetAllAssetNames();

		if (assetNames.Length == 0) {
			MPMain.LogWarning("警告：该 AssetBundle 是空的！");
			return;
		}

		foreach (string name in assetNames) {
			// 尝试加载资源以获取其实际类型（仅用于 Debug）
			Object asset = bundle.LoadAsset(name);
			string typeName = asset != null ? asset.GetType().Name : "Unknown Type";

			MPMain.LogInfo($"[资源清单] 路径: {name} | 类型: {typeName}");

			// 如果是文件夹或特殊的资源,有时需要额外注意
		}

		MPMain.LogInfo($"--- 清单输出完毕,共计 {assetNames.Length} 个资源 ---");
	}

	#endregion
}