using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Asus;

/// <summary>
/// 功 能： 读取 Omni 接收器上已核实的 ROG Scope II 96 键盘电量。
/// 2026/10/25 15:31:00 Mzood
/// </summary>
public sealed class AsusPlugin : BatteryPluginBase
{
    public override string Name => "Asus";
    public override string DisplayName => "华硕";
    public override string DisplayNameEn => "ASUS ROG";
    public override DeviceKind Kind => DeviceKind.Keyboard;
    protected override int MinValidPercent(HidDeviceInfo info) => 0;
    protected override OfflinePolicy Offline { get; } = new() { CountPollFailures = false };

    protected override bool IsCandidate(HidDeviceInfo info) =>
        AsusKeyboardCodec.IsKeyboardInterface(info.VendorId, info.ProductId, info.UsagePage,
            info.Usage, info.InputReportLength, info.OutputReportLength, info.OutputReportId, info.DevicePath);

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        if (!WritesEnabled || !VerifyPairedModel(handle.Info)) return null;
        var reading = QueryBattery(handle);
        return reading.HasValue ? ("asus-omni-12-01", reading.Value.Percent, reading.Value.Charging) : null;
    }

    private bool VerifyPairedModel(HidDeviceInfo keyboard)
    {
        string? key = AsusKeyboardCodec.ReceiverKey(keyboard.DevicePath);
        if (key == null) return false;
        var own = HidDeviceScanner.Scan().Where(x =>
            x.VendorId == 0x0B05 && x.ProductId == 0x1ACE
            && x.UsagePage is >= 0xFF00 and <= 0xFF02 && x.Usage == 1
            && x.InputReportLength == 64 && x.OutputReportLength == 64 && x.OutputReportId == 1
            && string.Equals(AsusKeyboardCodec.ReceiverKey(x.DevicePath), key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (own.Length != 1 || !WritesEnabled) return false;
        using var control = own[0].Open();
        var reply = Query(control, AsusKeyboardCodec.CreatePairedQuery(),
            r => AsusKeyboardCodec.HasScopeKeyboard(r));
        if (reply != null) return true;
        Log.Once(LogLevel.Warn, keyboard.DevicePath + "|asus-pair",
            "ROG Omni 未确认当前通道配对的是 Scope II 96，跳过电量查询");
        return false;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;
        var reading = QueryBattery(dev.Handle);
        if (reading.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return reading;
    }

    private (int? Percent, ChargingState Charging)? QueryBattery(HidDeviceHandle handle)
    {
        var reply = Query(handle, AsusKeyboardCodec.CreateBatteryQuery(), r =>
            AsusKeyboardCodec.TryParseBattery(r, out _, out _));
        if (reply == null || !AsusKeyboardCodec.TryParseBattery(reply, out int percent, out bool charging))
            return null;
        return (percent, ChargingStateMap.FromChargingFlag(charging, percent));
    }

    private byte[]? Query(HidDeviceHandle handle, byte[] request, Func<byte[], bool> matches)
    {
        if (!WritesEnabled) return null;
        for (int i = 0; i < 8; i++)
            if (handle.ReadInput(TimeSpan.FromMilliseconds(1)) == null) break;
        if (!WritesEnabled || !handle.WriteOutput(request, TimeSpan.FromMilliseconds(500))) return null;
        var until = DateTime.UtcNow.AddMilliseconds(700);
        while (DateTime.UtcNow < until)
        {
            var reply = handle.ReadInput(TimeSpan.FromMilliseconds(100));
            if (reply != null && matches(reply)) return reply;
        }
        return null;
    }

    protected override string BuildDisplayName(HidDeviceInfo info) => "ROG Strix Scope II 96 Wireless";
}
