using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VigilWatt.Core.Capture;

namespace VigilWatt.App.ViewModels;
/// <summary>
/// 功 能： 采集页的行模型（设备 / 接口 / 能力 / 记录 / 历史）。只做展示，不含业务判断。
/// 2026/10/24 12:40:43 20211
/// </summary>

/// <summary>一台物理设备（可能是 7 个 HID 接口）。</summary>
public sealed class CaptureDeviceRow
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    /// <summary>「未适配」/「已适配（雷柏）」/「全部打不开」，一眼区分能不能采。</summary>
    public required string Status { get; init; }

    public required CaptureDeviceGroup Group { get; init; }

    public override string ToString() => Title;
}

/// <summary>一台设备下的一个 HID 接口。</summary>
public sealed class CaptureInterfaceRow
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required bool IsRecommended { get; init; }

    public required CaptureDeviceInfo Info { get; init; }

    public override string ToString() => Title;
}

public sealed class CaptureCapabilityRow
{
    public required string Title { get; init; }

    public required string Status { get; init; }

    public required string Detail { get; init; }
}

/// <summary>实时记录里的一行：普通报文行，或「进入某一步」的分割行。</summary>
public sealed class CaptureRecordRow
{
    public required string Time { get; init; }

    public required string Kind { get; init; }

    public required string Hex { get; init; }

    public required string Note { get; init; }

    /// <summary>该协议通道的颜色（左侧色条 + 类型文字）。同一个「类型 + report ID」始终同色，扫一眼就能分开。</summary>
    public Brush? Accent { get; init; }

    /// <summary>非空 = 这是分割行（写这一步的标题），报文那几列不渲染。</summary>
    public string? Header { get; init; }

    public Visibility HeaderVisibility => string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility RecordVisibility => string.IsNullOrEmpty(Header) ? Visibility.Visible : Visibility.Collapsed;

    public static CaptureRecordRow StepHeader(string text) => new()
    {
        Time = "",
        Kind = "",
        Hex = "",
        Note = "",
        Header = text,
    };
}

public sealed class CaptureHistoryRow
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string Directory { get; init; }

    public required string ReportPath { get; init; }

    /// <summary>false = 会话没收尾（应用中途退出），只有原始记录可看。</summary>
    public required bool HasReport { get; init; }
}
