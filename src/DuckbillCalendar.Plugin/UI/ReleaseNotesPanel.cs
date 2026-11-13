using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DuckbillCalendar.Plugin;

internal sealed record PluginReleaseNote(string Version,string Title,IReadOnlyList<string> Changes);

internal static class PluginReleaseNotes
{
    public const string CurrentVersion = "0.3.2.0";
    public static IReadOnlyList<PluginReleaseNote> Entries { get; } = Array.AsReadOnly(new[]
    {
        new PluginReleaseNote(CurrentVersion,"최초 배포",Array.AsReadOnly(new[]
        {
            "한국 파이널판타지14의 주요 이벤트·업데이트·게임 서버 점검 일정을 달력에서 확인하고 공식 안내를 열 수 있습니다.",
            "개인 일정과 공격대 일정을 등록하고, 요일마다 다른 시각으로 여러 주의 일정을 한 번에 추가할 수 있습니다.",
            "이용권 만료일을 관리하고 일·시간·분 단위로 충전 기간을 더할 수 있습니다.",
            "내 분류를 만들고 일정의 아이콘·색상·표시 여부를 바꿀 수 있습니다. 미드나이트·차콜·라이트 테마를 제공합니다.",
            "최신 일정 자동 수신과 오늘·내일 전장 정보를 제공합니다. 전투·임무 중에는 자동 수신을 미룰 수 있습니다.",
            "새 공지·이벤트와 분류별·일정별 알림을 설정하고 알림 내역을 확인할 수 있습니다. 알림은 기본 꺼짐이며 설정에서 켤 수 있습니다."
        }))
    });
}

internal sealed partial class MainWindow
{
    private bool releaseNotesResetScroll;
    internal Vector2 ReleaseNotesSettingsTarget { get; private set; }
    internal Vector2 ReleaseNotesBackTarget { get; private set; }
    internal int ReleaseNotesRenderedEntries { get; private set; }

    public void OpenReleaseNotes()
    {
        IsOpen = true;
        selectSettings = false;
        activeTab = 6;
        releaseNotesResetScroll = true;
    }

    private void DrawReleaseNotes()
    {
        UiWidgets.Heading("업데이트 내역",Compact ? 1.15f : 1.5f);
        ContinueRow(ImGui.CalcTextSize("설정으로").X + ImGui.GetStyle().FramePadding.X * 2);
        if (ImGui.SmallButton("설정으로###releaseNotesBack")) OpenSettings();
        ReleaseNotesBackTarget = ItemCenter();
        ReleaseNotesRenderedEntries = 0;
        ImGui.BeginChild("###ReleaseNotesScroll",new(0,Math.Max(1,ImGui.GetContentRegionAvail().Y)));
        try
        {
            if (releaseNotesResetScroll) { ImGui.SetScrollY(0); releaseNotesResetScroll = false; }
            UiWidgets.Secondary("현재 버전 " + PluginReleaseNotes.CurrentVersion);
            ImGui.Spacing();
            foreach (var entry in PluginReleaseNotes.Entries)
            {
                UiWidgets.Colored(Accent,"v" + entry.Version + " · " + entry.Title);
                ImGui.Spacing();
                foreach (var change in entry.Changes)
                {
                    ImGui.Bullet(); ImGui.SameLine();
                    UiWidgets.Wrapped(change);
                    ImGui.Spacing();
                }
                ReleaseNotesRenderedEntries++;
                ImGui.Separator(); ImGui.Spacing();
            }
        }
        finally { ImGui.EndChild(); }
    }
}
