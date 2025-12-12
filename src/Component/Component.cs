using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using WKMultiMod.src.Core;
using static WKMultiMod.src.Core.MultiPlayerCore;

namespace WKMultiMod.src.Component;

// MultiPlayerComponent: 管理玩家的网络同步位置和旋转
public class MultiPlayerComponent : MonoBehaviour {
	public int id;
	private Vector3 _targetPosition;    // 目标位置
	private Vector3 _velocity = Vector3.zero;   // 当前速度，用于平滑插值

	void Update() {
		if (transform.position != _targetPosition) {
			// 动态计算平滑时间，确保最低速度
			float distance = Vector3.Distance(transform.position, _targetPosition); // 计算距离
			float smoothTime = Mathf.Max(0.1f, distance / 20f);	// 确保有最小速度

			transform.position = Vector3.SmoothDamp(
				transform.position,	// 当前位置
				_targetPosition,    // 目标位置
				ref _velocity,      // 速度引用
				smoothTime,         // 平滑时间
				float.MaxValue,     // 最大速度
				Time.deltaTime      // 时间增量
			);

			// 强制最低速度 2格/秒
			if (_velocity.magnitude < 2.0f && distance > 0.1f) {
				// 计算方向并设置最低速度
				Vector3 direction = (_targetPosition - transform.position).normalized;
				_velocity = direction * 2.0f;
			}
		}
	}

	public void UpdatePosition(Vector3 newPosition) {
		_targetPosition = newPosition;
	}

	public void UpdateRotation(Vector3 newRotation) {
		// 立即更新旋转
		transform.eulerAngles = newRotation;
	}
}


// MultiPlayerHandComponent: 管理玩家手部的网络同步位置
public class MultiPlayerHandComponent : MonoBehaviour {
	public int id;  // 玩家ID
	public HandType hand;    // 手部标识 (0: 左手, 1: 右手)

	private Vector3 _targetLocalPosition;   // 目标本地位置
	private Vector3 _velocity = Vector3.zero;   // 当前速度，用于平滑插值

	void Update() {
		if (transform.localPosition != _targetLocalPosition) {
			float distance = Vector3.Distance(transform.localPosition, _targetLocalPosition);
			float smoothTime = Mathf.Clamp(distance / 10f, 0.05f, 0.2f);

			transform.localPosition = Vector3.SmoothDamp(
				transform.localPosition,
				_targetLocalPosition,
				ref _velocity,
				smoothTime,
				float.MaxValue,
				Time.deltaTime
			);

			// 强制最低速度 0.5格/秒
			if (_velocity.magnitude < 0.5f && distance > 0.05f) {
				Vector3 direction = (_targetLocalPosition - transform.localPosition).normalized;
				_velocity = direction * 0.5f;
			}
		}
	}

	public void UpdateLocalPosition(Vector3 localPosition) {
		_targetLocalPosition = localPosition;
	}
}

// BillboardComponent: 使文本框始终面向摄像机
public class LootAtComponent : MonoBehaviour {
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

		// 使 Transform (文本框) 朝向摄像机
		transform.rotation = mainCamera.transform.rotation;

		// 使 Transform (文本框) y轴 朝向摄像机
		//transform.LookAt(transform.position + mainCamera.transform.rotation * Vector3.forward,
		//				 mainCamera.transform.rotation * Vector3.up);
	}
}