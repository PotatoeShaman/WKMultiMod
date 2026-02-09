using UnityEngine;
using WKMPMod.Util;

namespace WKMPMod.Test;

internal class TestMonoSingleton:MonoSingleton<TestMonoSingleton> {
	public string TestString = "这是一个测试单例组件类";
	protected override void Awake() {
		base.Awake();
		Debug.LogWarning("测试单例组件类初始化");
	}

	protected override void OnDestroy() {
		base.OnDestroy();
		Debug.LogWarning("测试单例组件类销毁");
	}
}
