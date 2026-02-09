using Steamworks.Data;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.Util;
using static WKMPMod.Data.MPReaderPool;
using static System.Buffers.Binary.BinaryPrimitives;

namespace WKMPMod.NetWork;

public class MPPacketRouter {
	// 路由表
	private static readonly Dictionary<PacketType, Delegate> Handlers;

	#region[初始化路由表]
	/// <summary>
	/// 初始化字典
	/// </summary>
	static MPPacketRouter() {
		// 反射MPPacketService类
		Handlers = typeof(MPPacketHandlers)
			// 获取所有静态方法, 包括非公共的
			.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
			// 获取带有 MPPacketHandlerAttribute 特性的 方法. false表示不搜索继承链, 只看当前方法
			.Where(method => method.GetCustomAttributes(typeof(MPPacketHandlerAttribute), false).Length > 0)
			// 对每个方法进行处理, 其生成多个IEnumerable<(PacketType, Delegate)>并扁平化
			.SelectMany(method => ProcessMethod(method))
			// 转换为字典, key为PacketType, value为对应的Delegate. 如果有重复的PacketType, 后者会覆盖前者
			.ToDictionary(t => t.Item1, t => t.Item2);
	}

	/// <summary>
	/// 处理反射获取的方法,转为IEnumerable<(PacketType, Delegate)> 迭代器<包类型,委托>
	/// </summary>
	private static IEnumerable<(PacketType, Delegate)> ProcessMethod(MethodInfo method) {
		// 获取方法上的所有 MPPacketHandlerAttribute 特性
		var attrs = method.GetCustomAttributes<MPPacketHandlerAttribute>();
		// 获取方法的参数信息
		var parameters = method.GetParameters();

		Delegate del = CreateDelegate(method, parameters);
		if (del == null) yield break;

		foreach (var attr in attrs) {
			yield return (attr.packetType, del);
		}
	}

	/// <summary>
	/// 将方法转为委托
	/// </summary>
	private static Delegate CreateDelegate(MethodInfo method, ParameterInfo[] parameters) {
		try {
			return parameters.Length switch {
				// 仅支持参数签名: (ulong, DataReader)
				2 when parameters[0].ParameterType == typeof(ulong) &&
					   parameters[1].ParameterType == typeof(DataReader)
					=> Delegate.CreateDelegate(typeof(Action<ulong, DataReader>), method),
				// 其余抛出异常
				_ => throw new InvalidOperationException($"Unsupported parameter signature for {method.Name}")
			};
		} catch (Exception e) {
			MPMain.LogError(Localization.Get(
				"MPPacketRouter", "FailedToBind", method.Name, e.Message));
			return null;
		}
	}
	#endregion

	#region[生命周期函数]
	public static void Initialize() {
		MPEventBusNet.OnReceiveData += Route;
	}
	#endregion

	#region[数据转换+路由]
	public static void Route(ulong connectionId, ArraySegment<byte> data) {
		// 确保数据足够读取一个整数(数据包类型)
		if (data.Array == null || data.Count < 18) return;

		// 直接解析头部
		ReadOnlySpan<byte> span = data;
		// 发送方ID
		ulong senderId = ReadUInt64LittleEndian(span);
		// 接收方ID
		ulong targetId = ReadUInt64LittleEndian(span.Slice(8));

		// 转发:目标不是我,也不是广播,也不是特殊判断ID
		if (targetId != MPSteamworks.Instance.UserSteamId
			&& targetId != MPSteamworks.Instance.BroadcastId
			&& targetId != MPSteamworks.Instance.SpecialId) {

			ProcessForwardToPeer(targetId, data);
			return; // 结束

		}

		// 广播:如果是广播,且不是我发出的
		if (targetId == MPSteamworks.Instance.BroadcastId
			&& senderId != MPSteamworks.Instance.UserSteamId) {

			ProcessBroadcastExcept(senderId, data);
			// 继续执行,因为主机也要处理广播包
		}

		// 包类型
		PacketType packetType = (PacketType)ReadUInt16LittleEndian(span.Slice(16));

		if (!Handlers.TryGetValue(packetType, out var handler)) {
			MPMain.LogError(Localization.Get("MPPacketRouter","NoServiceFound", packetType));
			return;
		}
		var reader = GetReader(data.Slice(18));

	}
	#endregion

	#region[网络发送工具类]
	/// <summary>
	/// 转发网络数据包到指定的客户端
	/// </summary>
	private static void ProcessForwardToPeer(ulong targetId, ArraySegment<byte> data) {
		// 直接从 segment 获取偏移和长度
		int offset = data.Offset;
		int count = data.Count;
		// 解析类型
		PacketType type = (PacketType)ReadUInt16LittleEndian(data.AsSpan(16, 2));
		SendType st = (type == PacketType.PlayerDataUpdate)
			? SendType.Unreliable : SendType.Reliable;

		MPSteamworks.Instance.SendToPeer(targetId, data.Array, offset, count, st);
	}

	/// <summary>
	/// 广播数据包到所有客户端
	/// </summary>
	public static void ProcessBroadcast(ArraySegment<byte> data) {
		// 直接从 segment 获取偏移和长度
		int offset = data.Offset;
		int count = data.Count;

		// 解析类型
		PacketType type = (PacketType)ReadUInt16LittleEndian(data.AsSpan(16, 2));

		SendType st = (type == PacketType.PlayerDataUpdate)
			? SendType.Unreliable : SendType.Reliable;

		// 将全套参数传给底层
		MPSteamworks.Instance.Broadcast(data.Array, offset, count, st);
	}

	/// <summary>
	/// 广播数据包到所有客户端 (除了发送者)
	/// </summary>
	/// <param name="senderId">发送方ID</param>
	public static void ProcessBroadcastExcept(ulong senderId, ArraySegment<byte> data) {
		// 直接从 segment 获取偏移和长度
		int offset = data.Offset;
		int count = data.Count;

		// 解析类型
		PacketType type = (PacketType)ReadUInt16LittleEndian(data.AsSpan(16, 2));

		SendType st = (type == PacketType.PlayerDataUpdate)
			? SendType.Unreliable : SendType.Reliable;

		// 将全套参数传给底层
		MPSteamworks.Instance.BroadcastExcept(senderId, data.Array, offset, count, st);
	}
	#endregion

}

// AttributeTargets ValidOn: 作用类型 AttributeTargets.Method: 作用于方法
// AllowMultiple: 是否允许同一方法上使用多个该属性,这里设置为 true 以支持一个方法处理多个消息类型
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class MPPacketHandlerAttribute : Attribute { 
	public PacketType packetType { get; }
	public MPPacketHandlerAttribute(PacketType packetType) {
		this.packetType = packetType;
	}
}