using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using WKMultiMod.src.Data;

namespace WKMultiMod.src.Core;

public static class NetworkEvents {
	// 发送事件：本地玩家数据 → 网络
	public static event Action<NetDataWriter> OnSendData;
	public static void TriggerSendData(NetDataWriter data)
		=> OnSendData?.Invoke(data);

	// 接收事件：网络 → 远程玩家
	public static event Action<NetPacketReader> OnReceiveData;
	public static void TriggerReceiveData(NetPacketReader data)
		=> OnReceiveData?.Invoke(data);

	// 连接事件
	public static event Action<ulong> OnPlayerConnected;
	public static event Action<ulong> OnPlayerDisconnected;
}