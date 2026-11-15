using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DuckbillCalendar.Plugin;

internal sealed record PluginReleaseNote(string Version, string Title, IReadOnlyList<string> Changes,
    string Summary = "", IReadOnlyList<string>? OtherChanges = null, string? Date = null);

internal static class PluginReleaseNotes
{
    public const string CurrentVersion = "0.5.0.0";
    public static IReadOnlyList<PluginReleaseNote> Entries { get; } = Array.AsReadOnly(new[]
    {
        new PluginReleaseNote(CurrentVersion, "화면과 업데이트 안내 정리", new[]
        {
            "스모크·펄의 화면 세부 조정에서 밝기·입체감·투명도를 바꾸면 실제 화면에 바로 반영됩니다.",
            "대표 기능과 기타 개선으로 정리한 업데이트 내역을 별도 화면에서 읽을 수 있습니다.",
            "설정에서 새 업데이트를 구분하고, 내역을 닫은 뒤에도 보고 있던 월과 선택 날짜를 유지합니다.",
            "날짜 상세를 연 상태에서도 다른 날짜를 한 번 눌러 바로 전환할 수 있습니다."
        }, "익숙한 달력에서 게임 UI에 어울리는 두 테마와 일관된 업데이트 안내를 사용할 수 있습니다.", new[]
        {
            "이미 확인한 업데이트는 이전 버전을 사용한 뒤에도 다시 새 항목으로 표시하지 않습니다.",
            "제목줄의 접기·닫기 버튼을 서로 겹치지 않는 영역에서 누를 수 있습니다.",
            "달력 기능을 앞세운 새 대표 아이콘으로 플러그인을 구분할 수 있습니다."
        }, "2026-11-15"),
        new PluginReleaseNote("0.4.0.1", "검색 안내 정리", new[]
        {
            "검색은 상단 검색 버튼으로 엽니다. 동작하지 않던 검색 단축키 안내를 제거했습니다."
        }, Date: "2026-11-15"),
        new PluginReleaseNote("0.4.0.0", "하우징 청약과 달력 검색", new[]
        {
            "하우징 신청·결과 확인·구매 확정 기간을 월 달력에서 확인하고 오늘·내일 마감을 구분할 수 있습니다.",
            "검색 버튼으로 일정 이름이나 버전 번호를 찾아 해당 날짜로 이동할 수 있습니다.",
            "보고 있는 달 밖이나 숨긴 일정도 기존 표시 설정을 바꾸지 않고 검색할 수 있습니다."
        }, "하우징 청약 기간을 확인하고 검색으로 원하는 일정을 바로 찾을 수 있습니다.", new[]
        {
            "선택 날짜와 오늘 표시를 구분하고 검색한 날짜가 보이도록 이동합니다.",
            "달력·설정·알림에서 상단 도구 위치와 선택 날짜를 유지합니다.",
            "주말·일정 표식과 글자 대비를 다듬고 긴 일정 제목은 한 줄로 표시합니다.",
            "하우징 일정은 에테르웨이의 한국 서버 주기 자료를 따릅니다. 일정 변경 시 게임 내 표시와 공식 공지를 우선 확인하세요."
        }, "2026-11-15"),
        new PluginReleaseNote("0.3.2.0", "최초 배포", new[]
        {
            "한국 파이널판타지14의 이벤트·업데이트·게임 서버 점검을 달력에서 확인하고 공식 안내를 열 수 있습니다.",
            "개인·공격대 일정을 등록하고 여러 주의 일정을 요일별 시각으로 추가할 수 있습니다.",
            "이용권 만료일을 관리하고 충전 기간을 더할 수 있습니다."
        }, "게임 일정과 개인 일정을 한 달력에서 관리할 수 있습니다.", new[]
        {
            "일정의 분류·아이콘·색상·표시 여부와 미드나이트·차콜·라이트 테마를 선택할 수 있습니다.",
            "최신 일정과 오늘·내일 전장 정보를 받고 전투·임무 중 자동 수신을 미룰 수 있습니다.",
            "새 공지·이벤트 및 일정별 알림을 설정할 수 있습니다. 알림은 기본 꺼짐입니다."
        }, "2026-11-14")
    });
}

internal sealed partial class MainWindow
{
    private bool releaseNotesOpen, releaseNotesResetScroll, releaseNotesContentShown, releaseNotesAcknowledged;
    internal Vector2 ReleaseNotesSettingsTarget { get; private set; }
    internal Vector2 ReleaseNotesBackTarget { get; private set; }
    internal int ReleaseNotesRenderedEntries { get; private set; }
    private bool HasUnreadReleaseNotes => !releaseNotesAcknowledged &&
        (!Version.TryParse(model.State.View.LastSeenChangelogVersion, out var seen) || seen < Version.Parse(PluginReleaseNotes.CurrentVersion));

    public void OpenReleaseNotes()
    {
        if (releaseNotesOpen) return;
        releaseNotesOpen = true;
        releaseNotesResetScroll = true;
        releaseNotesContentShown = false;
    }

    // Draw independently of the calendar. Opening notes never changes the month, selection or active page.
    public void DrawReleaseNotesWindow()
    {
        if (!releaseNotesOpen) return;
        var f = ImGui.GetFontSize();
        var viewport = ImGui.GetMainViewport();
        var available = new Vector2(Math.Max(1, viewport.WorkSize.X - 32), Math.Max(1, viewport.WorkSize.Y - 32));
        ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize / 2, ImGuiCond.FirstUseEver, new(.5f));
        ImGui.SetNextWindowSize(new(Math.Min(f * 36, available.X), Math.Min(f * 32, available.Y)), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new(Math.Min(f * 21, available.X), Math.Min(f * 17, available.Y)), available);
        ImGui.SetNextWindowBgAlpha(GameThemePalette.OverlayOpacity);
        var visible = ImGui.Begin(UiText.Get("UpdatesTitle") + "###DuckbillCalendar.Changelog", ref releaseNotesOpen,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        try
        {
            if (visible)
            {
                using var contentStyle = GameThemeChrome.Content();
                GameThemeChrome.WindowSurface();
                if (ImGui.BeginChild("###ReleaseNotesScroll", new(0, -ImGui.GetFrameHeightWithSpacing())))
                {
                    if (releaseNotesResetScroll) { ImGui.SetScrollY(0); releaseNotesResetScroll = false; }
                    UiWidgets.Wrapped(UiText.Get("UpdatesVersion", PluginReleaseNotes.CurrentVersion));
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                    UiWidgets.Wrapped(UiText.Get("UpdatesOffline", PluginReleaseNotes.Entries.Count));
                    ImGui.PopStyleColor();
                    ImGui.Spacing();
                    ReleaseNotesRenderedEntries = 0;
                    foreach (var entry in PluginReleaseNotes.Entries)
                    {
                        ImGui.SetNextItemOpen(entry.Version == PluginReleaseNotes.CurrentVersion, ImGuiCond.Once);
                        if (!GameThemeChrome.Disclosure(UiText.Get("UpdatesRelease", entry.Version) + "###Release." + entry.Version)) continue;
                        UiWidgets.Secondary(entry.Date is { } date ? UiText.Get("UpdatesDate", date) : UiText.Get("UpdatesDevelopment"));
                        ImGui.Spacing();
                        GameThemeChrome.FeatureTitle(entry.Title);
                        if (entry.Summary.Length > 0) UiWidgets.Wrapped(entry.Summary);
                        ImGui.Spacing();
                        foreach (var change in entry.Changes) ReleaseEntry(change);
                        if (entry.OtherChanges is { Count: > 0 } other)
                        {
                            GameThemeChrome.SectionLabel(UiText.Get("UpdatesOther"));
                            foreach (var change in other) ReleaseEntry(change);
                        }
                        if (entry.Version == PluginReleaseNotes.CurrentVersion) releaseNotesContentShown = true;
                        ReleaseNotesRenderedEntries++;
                        ImGui.Spacing();
                    }
                }
                ImGui.EndChild();
                if (GameThemeChrome.Button(UiText.Get("UpdatesDismiss") + "###DismissChangelog")) releaseNotesOpen = false;
                ReleaseNotesBackTarget = ItemCenter();
                if (model.Workspace.HasUnsavedChanges)
                {
                    ImGui.SameLine(); UiWidgets.Secondary(UiText.Get("UpdatesUnsaved"));
                }
            }
        }
        finally { ImGui.End(); }
        if (!releaseNotesOpen && releaseNotesContentShown)
        {
            releaseNotesAcknowledged = true;
            model.UpdateView(v => Version.TryParse(v.LastSeenChangelogVersion, out var seen) && seen >= Version.Parse(PluginReleaseNotes.CurrentVersion)
                ? v : v with { LastSeenChangelogVersion = PluginReleaseNotes.CurrentVersion });
        }
    }

    private static void ReleaseEntry(string text) => GameThemeChrome.Note(text);

}
