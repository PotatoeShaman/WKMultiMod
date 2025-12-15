using LiteNetLib.Utils;
using Steamworks;
using System;
using UnityEngine;
using static WKMultiMod.src.Data.PlayerData;

namespace WKMultiMod.src.Data;



[System.Serializable]
public class PlayerData {
	public enum HandType { Left = 0, Right = 1 }

	// 玩家ID
	public int playId;
	// 时间戳(网络同步关键)
	public long TimestampTicks;
	// 位置和旋转(直接用float字段)
	public float PosX, PosY, PosZ;
	public float RotX, RotY, RotZ, RotW;
	// 手部数据
	public HandData LeftHand;
	public HandData RightHand;
	// 特殊标志
	public bool IsTeleport;

	// 辅助属性(不序列化)
	[System.NonSerialized] private Vector3 _positionCache;
	[System.NonSerialized] private Quaternion _rotationCache;

	// PlayerId(4) + TimestampTicks(8) + 位置(12) + 旋转(16) + 
	// 左手(13) + 右手(13) + IsTeleport(1)
	// 包长度
	public static int CalculateSize => 4 + 8 + 12 + 16 + 13 + 13 + 1;

	public Vector3 Position {
		get {
			// 避免每次都new,但要注意线程安全
			if (_positionCache == default)
				_positionCache = new Vector3(PosX, PosY, PosZ);
			return _positionCache;
		}
		set {
			_positionCache = value;
			PosX = value.x; PosY = value.y; PosZ = value.z;
		}
	}

	public Quaternion Rotation {
		get {
			if (_rotationCache == default)
				_rotationCache = new Quaternion(RotX, RotY, RotZ, RotW);
			return _rotationCache;
		}
		set {
			_rotationCache = value;
			RotX = value.x; RotY = value.y; RotZ = value.z; RotW = value.w;
		}
	}

	public DateTime Timestamp {
		get => new DateTime(TimestampTicks);
		set => TimestampTicks = value.Ticks;
	}

	// 构造函数
	public PlayerData() {
		LeftHand = new HandData { handType = HandType.Left };
		RightHand = new HandData { handType = HandType.Right };
	}
}

[System.Serializable]
public class HandData {
	// 手部类型
	public PlayerData.HandType handType;
	// 是否空闲
	public bool IsFree;
	// 位置
	public float PosX;
	public float PosY;
	public float PosZ;

	[System.NonSerialized] private Vector3 _positionCache;

	public Vector3 Position {
		get {
			if (_positionCache == default)
				_positionCache = new Vector3(PosX, PosY, PosZ);
			return _positionCache;
		}
		set {
			_positionCache = value;
			PosX = value.x;
			PosY = value.y;
			PosZ = value.z;
		}
	}
}

