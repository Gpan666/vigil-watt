using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Lingbao;

/// <summary>
/// 功 能： 凌豹 (LINGBAO) M5PRO Max 2.4G 鼠标插件，feature 通道查询电量。
/// 2026/10/24 17:42:00 L
/// 帧结构与本机验证边界见 docs/hardware/lingbao.md §1；会写帧，故默认关闭。
/// </summary>
public sealed class LingbaoPlugin : BatteryPluginBase
{
    public const ushort LingbaoVid = 0x3151;

    /// <summary>2.4G 接收器（本机实测 VID_3151&amp;PID_402D）。有线形态 4026 只回显命令、不返回状态，故不认领。</summary>
    public const ushort M5ProMaxDonglePid = 0x402D;

    /// <summary>厂商集合：usage page 0xFFFF、usage 2、feature 65 字节。</summary>
    private const ushort VendorUsagePage = 0xFFFF;

    private const ushort VendorUsage = 0x0002;

    /// <summary>查询帧已由官方驱动抓包确认（`f7`），但只有一条命令，故不参与 15 秒发现节拍的额外写入。</summary>
    public override bool SupportsDiscoveryProbe => true;

    /// <summary>10 秒 &lt; 15 秒节拍：每次探测最多 1 帧查询，代价可忽略（理由同 Pulsar）。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    public override string Name => "Lingbao";
    public override string DisplayName => "凌豹";
    public override string DisplayNameEn => "LINGBAO";
    public override DeviceKind Kind => DeviceKind.Mouse;

    /// <summary>鼠标休眠时接口静音是常态；null 的含义由插件自己报账，不交给基类猜。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == LingbaoVid
           && info.ProductId == M5ProMaxDonglePid
           && info.UsagePage == VendorUsagePage && info.Usage == VendorUsage
           && info.FeatureReportLength >= LingbaoCodec.ReportLength;

    /// <summary>探测 = 发一条状态查询，读回同 ID 的响应。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var read = Query(handle);
        return read is { } value ? ("lingbao-fea", value.Percent, value.Charging) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var read = Query(dev.Handle);
        if (read.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return read;
    }

    /// <summary>产品名是泛化的「2.4G Wireless Mouse」，型号只能按 PID 写死。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info) => "凌豹 M5PRO Max";

    /// <summary>SetFeature 后设备约 10 ms 才刷新响应；读太早会拿到上一条状态块。</summary>
    private static (int? Percent, ChargingState Charging)? Query(HidDeviceHandle handle)
    {
        if (handle.Info.FeatureReportLength < LingbaoCodec.ReportLength) return null;
        if (!handle.SetFeature(LingbaoCodec.ReportId, LingbaoCodec.BuildStatusQuery()))
        {
            Log.Once(LogLevel.Debug, LogKey(handle), $"凌豹 状态查询写入失败: {handle.Info.DevicePath}");
            return null;
        }

        Thread.Sleep(20);
        var report = handle.GetFeature(LingbaoCodec.ReportId);
        if (report == null || !LingbaoCodec.TryParseStatus(report, out int percent, out bool charging))
        {
            // 只记一次：发现节拍每 15 秒重探，鼠标睡着时逐次记会把日志刷满
            Log.Once(LogLevel.Debug, LogKey(handle), $"凌豹 响应里没有可信电量: {Log.Hex(report ?? [])}");
            return null;
        }

        Log.ResetOnce(LogKey(handle));
        Log.Info($"凌豹 状态查询: 电量={percent}% 充电={charging} 原始={Log.Hex(report)}");
        return (percent, ChargingStateMap.FromChargingFlag(charging, percent));
    }

    private static string LogKey(HidDeviceHandle handle) => $"{handle.Info.DevicePath}|lingbao";
}
