using System.Collections.Concurrent;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Razer;

/// <summary>
/// 功 能： 雷蛇 2.4G 鼠标插件 —— 走 vendor feature report 轮询电量。
/// 2026/10/24 23:45:00 imx1n
///
/// 与纯被动插件不同，**这个插件会向设备写帧**：开「雷蛇」开关即授权发送电量/充电查询帧。
/// 帧结构与校验口径（XOR，不是 CRC-8）见 <see cref="RazerCodec"/> 与 docs/hardware/razer.md。
/// </summary>
public sealed class RazerPlugin : BatteryPluginBase
{
    public const ushort RazerVid = RazerCodec.RazerVid;

    /// <summary>按 PID 记住哪个事务号有应答，后续轮询只发一遍（探不到就每轮重试两个候选）。</summary>
    private static readonly ConcurrentDictionary<ushort, byte> LearnedTransaction = new();

    public override string Name => "Razer";
    public override string DisplayName => "雷蛇";
    public override string DisplayNameEn => "RAZER";
    public override DeviceKind Kind => DeviceKind.Mouse;

    /// <summary>
    /// 会写帧，但仍要参与 15 秒发现节拍：否则关灯休眠再唤醒后最坏要等一整轮 60 秒才被认领。
    /// 安全前提由 <see cref="ProbeCooldown"/> 兜住（必须明显小于 15 秒）。
    /// </summary>
    public override bool SupportsDiscoveryProbe => true;

    /// <summary>失败后 8 秒再探：鼠标休眠时接收器不应答，醒来要能很快重新认领。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(8);

    /// <summary>
    /// 查询型插件：轮询空读＝设备失联（休眠/关机/接收器拔掉）。
    /// 不按"沉默"判离线——本协议没有自发推流。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Confirm = TimeSpan.FromSeconds(6),
        Silence = TimeSpan.Zero,
    };

    /// <summary>
    /// 认领判据：VID + 已知 PID + feature 报文正好 91 字节。
    /// **同一台设备还有一条 62 字节的厂商接口（usage 0xFF13），别认错**：
    /// 经典协议挂在"消费控制"接口（usage page 0x0C）上，长度判据正好把两者分开。
    /// </summary>
    protected override bool IsCandidate(HidDeviceInfo info)
        => RazerCodec.IsSupported(info.VendorId, info.ProductId, info.FeatureReportLength);

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => Query(handle, handle.Info.ProductId);

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        // R1：每个写帧点都要能被开关挡住。基类只在 Probe 阶段拦，轮询这里得自己问（含熔断退避）。
        if (!CanWrite(dev)) return null;

        var reading = Query(dev.Handle, dev.Info.ProductId);
        return reading.HasValue ? (reading.Value.Percent, reading.Value.Charging) : null;
    }

    /// <summary>
    /// 一轮查询 = 电量帧 +（可选）充电帧；全部走同一个 feature report ID。
    ///
    /// 无线鼠标空闲时会休眠，接收器拿不到应答，返回 <c>status=0x04</c>（无响应）且参数全 0。
    /// 这**不是错误**，只说明"鼠标睡着了"，所以最多重试两轮，仍无应答就交给离线策略，
    /// 别把它记成写入故障（否则日志会被刷爆）。
    /// </summary>
    private static (string Protocol, int? Percent, ChargingState Charging)? Query(HidDeviceHandle handle, ushort pid)
    {
        var candidates = LearnedTransaction.TryGetValue(pid, out byte learned)
            ? new[] { learned }
            : RazerCodec.TransactionCandidates(pid);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            foreach (byte txn in candidates)
            {
                if (!handle.SetFeature(RazerCodec.ReportId, RazerCodec.BuildRequest(RazerCodec.CommandGetBattery, txn)))
                    continue;

                var response = handle.GetFeature(RazerCodec.ReportId);
                if (!RazerCodec.TryParseBattery(response, out int raw))
                    continue;

                LearnedTransaction[pid] = txn;
                int percent = RazerCodec.ToPercent(pid, raw);

                var charging = ChargingState.Unknown;
                if (handle.SetFeature(RazerCodec.ReportId, RazerCodec.BuildRequest(RazerCodec.CommandGetCharging, txn)))
                {
                    var chargeResponse = handle.GetFeature(RazerCodec.ReportId);
                    if (RazerCodec.TryParseCharging(chargeResponse, out var state))
                        charging = state;
                }

                // 100% 且仍报充电中，对用户来说就是"已充满"。
                if (percent >= 100 && charging == ChargingState.Charging)
                    charging = ChargingState.Full;

                return ("razer-hid", percent, charging);
            }

            if (attempt == 0)
                Thread.Sleep(40);
        }

        return null;
    }
}
