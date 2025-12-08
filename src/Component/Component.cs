using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WKMultiMod.src.Component;

// MultiPlayerComponent: 管理玩家的网络同步位置和旋转
public class MultiPlayerComponent : MonoBehaviour {
	int id;  // 玩家ID, 用于在网络中识别不同的玩家实例

	// 更新玩家位置的方法
	public void UpdatePosition(Vector3 new_position) {
		// 实际更新游戏对象的位置
		transform.position = new_position;
	}

	// 更新玩家旋转的方法
	public void UpdateRotation(Vector3 new_rotation) {
		// 设置游戏对象的欧拉角旋转
		transform.eulerAngles = new_rotation;
	}
}

// BillboardComponent: 使文本框始终面向摄像机
public class BillboardComponent : MonoBehaviour {
	private Camera mainCamera;
	void LateUpdate() {
		// 持续检查并尝试获取主摄像机
		if (mainCamera == null) {
			mainCamera = Camera.main;

			// 如果仍然找不到，则跳过本帧
			if (mainCamera == null) {
				// Debug.LogWarning("Waiting for Main Camera..."); 
				return;
			}
		}

		// 使 Transform (文本框) 面对摄像机
		transform.LookAt(transform.position + mainCamera.transform.rotation * Vector3.forward,
						 mainCamera.transform.rotation * Vector3.up);
	}
}