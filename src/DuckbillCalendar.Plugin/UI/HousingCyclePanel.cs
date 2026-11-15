using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal sealed partial class MainWindow
{
    internal Vector2 HousingCycleTarget { get; private set; }
    internal bool HousingCycleFits { get; private set; }
    internal string HousingPhaseLabel { get; private set; } = "";
    internal string HousingDeadlineLabel { get; private set; } = "";
    internal Dictionary<DateOnly, Vector2> HousingDayTargets { get; } = [];
    internal Dictionary<DateOnly, string> HousingDayCaptions { get; } = [];
    internal bool HousingDayDetailShown { get; private set; }
    internal bool HousingPopupVisible { get; private set; }
    internal Vector2 HousingPopupPosition { get; private set; }
    internal Vector2 HousingPopupSize { get; private set; }

    private void DrawHousingCycle()
    {
        HousingCycleTarget = Vector2.Zero;
        HousingPopupVisible = false;
        if (!model.IsCalendarVisible(CalendarGroup.Housing)) return;
        var f = ImGui.GetFontSize(); var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var pos = ImGui.GetCursorScreenPos(); var size = new Vector2(width, f * 1.45f);
        var current = model.Housing;
        HousingPhaseLabel = current?.Phase switch
        {
            HousingLotteryPhase.Entry => "신청 중", HousingLotteryPhase.Results => "결과·구매 확정 중", _ => "출처 기준일 이전"
        };
        HousingDeadlineLabel = current?.EndsAt is { } deadline ? "마감 " + UiText.Time(deadline) + " KST" : "임무 정보 목록 → 하우징에서 확인";
        if (ImGui.InvisibleButton("###HousingCycle", size)) ImGui.OpenPopup("###HousingCycleDetails");
        HousingCycleTarget = pos + size / 2;
        var draw = ImGui.GetWindowDrawList();
        var style = CalendarAppearanceStyle.Resolve(model.State.View, "housing");
        draw.PushClipRect(pos,pos+size,true);
        UiWidgets.DrawIcon(style.Icon,pos+new Vector2(f*.2f,f*.15f),f*.85f,ImGui.ColorConvertFloat4ToU32(style.Color));
        var label = "하우징 · " + HousingPhaseLabel + (current?.EndsAt is not null ? " · " + HousingLotterySchedule.DescribeDay(current, model.Today, model.Today).Detail : "");
        var labelColor = current is null ? ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled] : HousingPhaseColor(current.Phase);
        draw.AddText(pos+new Vector2(f*1.4f,f*.05f),ImGui.ColorConvertFloat4ToU32(labelColor),FitLabel(label,width-f*1.6f));
        draw.PopClipRect();
        HousingCycleFits = pos.X + size.X <= ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X + .5f;
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip(HousingPhaseLabel+"\n"+HousingDeadlineLabel+"\n클릭하면 기간과 출처를 확인합니다.");
        var viewport = ImGui.GetMainViewport(); var work = viewport.WorkSize;
        var popupWidth = Math.Min(f * 29, work.X - f * 2);
        ImGui.SetNextWindowSizeConstraints(new(popupWidth, 0), new(popupWidth, Math.Max(f * 6, work.Y - f * 2)));
        var popupHeight = HousingPopupSize.Y > 0 ? Math.Min(HousingPopupSize.Y, work.Y - f * 2) : Math.Min(f * 15, work.Y - f * 2);
        ImGui.SetNextWindowPos(new(
            Math.Clamp(pos.X, viewport.WorkPos.X + f, Math.Max(viewport.WorkPos.X + f, viewport.WorkPos.X + work.X - popupWidth - f)),
            Math.Clamp(pos.Y + size.Y, viewport.WorkPos.Y + f, Math.Max(viewport.WorkPos.Y + f, viewport.WorkPos.Y + work.Y - popupHeight - f))));
        if (ImGui.BeginPopup("###HousingCycleDetails"))
        {
            try
            {
                HousingPopupVisible = true;
                HousingPopupPosition = ImGui.GetWindowPos(); HousingPopupSize = ImGui.GetWindowSize();
                ImGui.TextUnformatted("하우징 청약"); ImGui.Separator();
                UiWidgets.Wrapped(HousingPhaseLabel + " · " + HousingDeadlineLabel);
                if (current is { } period)
                    MutedWrapped("기간  " + period.StartsAt.ToString("MM.dd HH:mm") + " ~ " + period.EndsAt.ToString("MM.dd HH:mm") + " KST");
                MutedWrapped("신청 5일 → 결과·구매 확정 4일");
                if (current?.Phase == HousingLotteryPhase.Results)
                    UiWidgets.Wrapped("당첨자는 결과 발표 기간 안에 토지 간판에서 구매를 확정하세요.");
                ImGui.Spacing(); ImGui.Separator();
                MutedWrapped(HousingLotterySchedule.SourceName + " · " + HousingLotterySchedule.SourceCheckedOn + " 확인");
                MutedWrapped("일정이 변경되면 게임 내 표시와 공식 공지가 우선합니다.");
                if (GameThemeChrome.Button("주기 출처 ↗")) model.Post(() => Util.OpenLink(HousingLotterySchedule.SourceUrl));
                ContinueRow(ImGui.CalcTextSize("공식 안내 ↗").X + ImGui.GetStyle().FramePadding.X * 2);
                if (GameThemeChrome.Button("공식 안내 ↗")) model.Post(()=>Util.OpenLink(HousingLotterySchedule.GuideUrl));
            }
            finally { ImGui.EndPopup(); }
        }
    }

    private Vector4 HousingPhaseColor(HousingLotteryPhase phase)
    {
        return phase == HousingLotteryPhase.Entry ? CalendarAppearanceStyle.Resolve(model.State.View,"housing").Color : ThemeScope.Hex(Light?0x386b80:0x91bfd3);
    }
    private static string PhaseName(HousingLotteryPhase phase) => phase == HousingLotteryPhase.Entry ? "청약 신청" : "결과·구매 확정";

    private void DrawHousingDayBackground(DateOnly date, Vector2 pos, Vector2 end, HousingLotteryPeriod period)
    {
        var color = HousingPhaseColor(period.Phase);
        var alpha = date < model.Today ? .012f : date == model.Today ? .065f : .025f;
        ImGui.GetWindowDrawList().AddRectFilled(pos + Vector2.One, end - Vector2.One, ImGui.ColorConvertFloat4ToU32(color with { W = alpha }));
    }

    private void DrawHousingDayContent(DateOnly date, Vector2 pos, Vector2 end, HousingLotteryPeriod period)
    {
        var f = ImGui.GetFontSize(); var draw = ImGui.GetWindowDrawList(); var color = HousingPhaseColor(period.Phase);
        var label = HousingLotterySchedule.DescribeDay(period, date, model.Today);
        var near = date >= model.Today && date.DayNumber <= model.Today.DayNumber + 1;
        var urgent = near && label.Urgent;
        var caption = urgent ? (period.Phase == HousingLotteryPhase.Entry ? "신청" : "결과") + " · " + label.Detail
            : label.DeadlineDay && date >= model.Today ? period.Phase == HousingLotteryPhase.Entry ? "청약 신청 마감" : "구매 확정 마감" : label.Title;
        var scale = urgent ? .8f : .73f;
        var min = new Vector2(pos.X + f * .45f, end.Y - f * 1.08f);
        var width = Math.Max(1, end.X - min.X - f * .35f);
        var tint = urgent ? ThemeScope.Hex(Light ? 0x9b3e14 : 0xffcd8e) : color with { W = date < model.Today ? .45f : .72f };
        draw.PushClipRect(pos, end, true);
        if (urgent)
        {
            var badgeWidth = Math.Min(width, ImGui.CalcTextSize(caption).X * scale + f * .4f);
            draw.AddRectFilled(min - new Vector2(f * .12f, 0), min + new Vector2(badgeWidth, f * .98f), ImGui.ColorConvertFloat4ToU32(tint with { W = .12f }), 3);
        }
        draw.AddCircleFilled(min + new Vector2(f * .1f, f * .43f), f * .075f, ImGui.ColorConvertFloat4ToU32(color with { W = date < model.Today ? .35f : .8f }));
        draw.AddText(ImGui.GetFont(), f * scale, min + new Vector2(f * .3f, 0), ImGui.ColorConvertFloat4ToU32(tint), FitLabel(caption, (width - f * .3f) / scale));
        draw.PopClipRect();
        HousingDayTargets[date] = min + new Vector2(width / 2, f * .45f);
        HousingDayCaptions[date] = caption;
        var mouse = ImGui.GetIO().MousePos;
        if (ImGui.IsWindowHovered() && mouse.X >= pos.X && mouse.X <= end.X && mouse.Y >= min.Y && mouse.Y <= end.Y)
            UiWidgets.Tooltip(PhaseName(period.Phase) + "\n마감 " + UiText.Time(period.EndsAt) + " KST\n" + HousingLotterySchedule.SourceName + " 주기 · 날짜를 누르면 자세히 봅니다.");
    }
    private void DrawHousingDayDetail(DateOnly date)
    {
        HousingDayDetailShown=false;
        if(model.HousingForDate(date) is not { } observation) return;
        HousingDayDetailShown=true;
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text,HousingPhaseColor(observation.Phase));
        try { UiWidgets.Wrapped("하우징 · "+PhaseName(observation.Phase)); }
        finally { ImGui.PopStyleColor(); }
        UiWidgets.Wrapped("마감 "+UiText.Time(observation.EndsAt)+" KST");
        MutedWrapped("출처: " + HousingLotterySchedule.SourceName + " · 한국 서버 주기");
        if(observation.Phase==HousingLotteryPhase.Results) MutedWrapped("당첨자는 기간 안에 토지 간판에서 구매를 확정하세요.");
        ImGui.Separator();
    }
}
