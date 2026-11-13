using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DuckbillCalendar.Infrastructure;
using DuckbillCalendar.Core;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DuckbillCalendar.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly ICondition conditions;
    private readonly WindowSystem windows = new("DuckbillCalendar");
    // Draw and Framework callbacks must not concurrently mutate the model, workspace
    // completions or frontline dictionaries. IO continues on their own workers.
    private readonly object stateGate = new();
    private readonly GameActivityPolicy activityPolicy = new();
    private readonly List<string> registeredCommands = [];
    private readonly CalendarWorkspace workspace = null!;
    private readonly CalendarModel model = null!;
    private readonly NotificationCoordinator alerts = null!;
    private readonly MainWindow main = null!;
    private readonly FrontlineScheduleClient frontline = null!;
    private bool drawSubscribed;
    private bool mainSubscribed;
    private bool configSubscribed;
    private bool frameworkSubscribed;
    private bool disposed;
    private DateTimeOffset nextErrorLog;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager, IFramework framework,
        IPluginLog log, IChatGui chat, IClientState clientState, IPlayerState playerState, ICondition conditions, ITextureProvider textures)
    {
        this.pluginInterface = pluginInterface;
        commands = commandManager;
        this.framework = framework;
        this.log = log;
        this.chat = chat;
        this.clientState = clientState;
        this.playerState = playerState;
        this.conditions = conditions;
        try
        {
            using var resource = typeof(Plugin).Assembly.GetManifestResourceStream("DuckbillCalendar.ExampleFeed")
                ?? throw new InvalidDataException("MissingBundledExample");
            using var example = new MemoryStream();
            resource.CopyTo(example);
            using var snapshotResource = typeof(Plugin).Assembly.GetManifestResourceStream("DuckbillCalendar.OfficialSnapshot")
                ?? throw new InvalidDataException("MissingOfficialSnapshot");
            using var snapshot = new MemoryStream();
            snapshotResource.CopyTo(snapshot);
            workspace = new CalendarWorkspace(pluginInterface.GetPluginConfigDirectory(), example.ToArray(), null, bundledFeed: snapshot.ToArray());
            model = new CalendarModel(workspace);
            frontline = new FrontlineScheduleClient(directory: pluginInterface.GetPluginConfigDirectory());
            alerts = new NotificationCoordinator(workspace);
            UiText.SetLanguage(workspace.State.View.Language);
            main = new MainWindow(model, frontline, (pos,size,alpha) =>
            {
                var icon = textures.GetFromManifestResource(typeof(Plugin).Assembly,"DuckbillCalendar.BittyDuckbill").GetWrapOrDefault();
                if (icon is null) return false;
                ImGui.GetWindowDrawList().AddImage(icon.Handle,pos,pos + new Vector2(size),Vector2.Zero,Vector2.One,ImGui.ColorConvertFloat4ToU32(new(1,1,1,alpha)));
                return true;
            });
            windows.AddWindow(main);
            var info = new CommandInfo(OnCommand) { HelpMessage = UiText.Get("CommandHelp") };
            foreach (var command in new[] { "/dbcal", "/duckbillcalendar" })
            {
                if (!commands.AddHandler(command, info)) throw new InvalidOperationException("CommandAlreadyRegistered:" + command);
                registeredCommands.Add(command);
            }
            pluginInterface.UiBuilder.Draw += Draw;
            drawSubscribed = true;
            pluginInterface.UiBuilder.OpenMainUi += OpenMain;
            mainSubscribed = true;
            pluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
            configSubscribed = true;
            framework.Update += Update;
            frameworkSubscribed = true;
            lock (stateGate)
            {
                UpdateActivity(DateTimeOffset.UtcNow);
                model.Tick(DateTimeOffset.UtcNow);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OnCommand(string command, string arguments)
    {
        lock (stateGate)
        {
            if (disposed) return;
            UpdateActivity(DateTimeOffset.UtcNow);
            switch (arguments.Trim().ToLowerInvariant())
            {
                case "settings": main.OpenSettings(); break;
                case "changelog": main.OpenReleaseNotes(); break;
                default:
                    main.IsOpen = !main.IsOpen;
                    break;
            }
        }
    }
    private void OpenMain() { lock (stateGate) { if (!disposed) { main.IsOpen = true; } } }
    private void OpenSettings() { lock (stateGate) { if (!disposed) { main.OpenSettings(); } } }
    private void Draw()
    {
        lock (stateGate)
        {
            if (disposed) return;
            UpdateActivity(DateTimeOffset.UtcNow);
            using var theme = new ThemeScope(model.State.View.Theme);
            windows.Draw();
            main.DrawFloatingWindows();
        }
    }
    private void Update(IFramework _)
    {
        lock (stateGate)
        {
            if (disposed) return;
            try
            {
                var now = DateTimeOffset.UtcNow;
                UpdateActivity(now);
                model.Tick(now);
                frontline.Tick(now, model.State.View.FrontlineUpdatesEnabled, model.Year, model.MonthNumber, model.AutomaticUpdatesAllowed);
                var session = clientState.IsLoggedIn && playerState.IsLoaded && playerState.ContentId != 0
                    ? playerState.ContentId.ToString(CultureInfo.InvariantCulture) : null;
                var canDeliver = !clientState.IsPvP && !conditions[ConditionFlag.InCombat] &&
                    !conditions[ConditionFlag.BoundByDuty] && !conditions[ConditionFlag.BoundByDuty56] &&
                    !conditions[ConditionFlag.BoundByDuty95] && !conditions[ConditionFlag.BetweenAreas] &&
                    !conditions[ConditionFlag.BetweenAreas51] && !conditions[ConditionFlag.OccupiedInCutSceneEvent] &&
                    !conditions[ConditionFlag.WatchingCutscene] && !conditions[ConditionFlag.WatchingCutscene78];
                alerts.Tick(now, session, canDeliver, items =>
                {
                    static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
                    chat.Print("[오리부리 달력] " + (items.Count == 1 ? Clean(items[0].Title) + " · " + Clean(items[0].Message) +
                        (items[0].BoundaryAt is { } at ? " · " + UiText.Time(at) : "") : "알림 " + items.Count + "건 · " + string.Join(" / ", items.Take(3).Select(n => Clean(n.Title)))));
                });
            }
            catch (Exception ex)
            {
                var now = DateTimeOffset.UtcNow;
                if (now >= nextErrorLog)
                {
                    log.Error(ex, "DuckbillCalendar update failed; previous view retained");
                    nextErrorLog = now.AddMinutes(1);
                }
            }
        }
    }
    private void UpdateActivity(DateTimeOffset now)
    {
        var activity = new GameActivity(clientState.IsLoggedIn, playerState.IsLoaded, conditions[ConditionFlag.InCombat],
            conditions[ConditionFlag.BoundByDuty] || conditions[ConditionFlag.BoundByDuty56] || conditions[ConditionFlag.BoundByDuty95],
            clientState.IsPvP, conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51],
            conditions[ConditionFlag.OccupiedInCutSceneEvent] || conditions[ConditionFlag.WatchingCutscene] || conditions[ConditionFlag.WatchingCutscene78], clientState.TerritoryType);
        activityPolicy.Update(now, activity, model.State.View);
        var message = activityPolicy.Hold switch
        {
            AutomaticUpdateHold.LoggedOut => "접속 후 자동 갱신을 재개합니다.", AutomaticUpdateHold.Loading => "지역 이동 중에는 자동 갱신을 기다립니다.",
            AutomaticUpdateHold.Cutscene => "영상이 끝나면 자동 갱신을 재개합니다.", AutomaticUpdateHold.Combat => "전투 중에는 자동 갱신을 기다립니다.",
            AutomaticUpdateHold.Duty => "임무에서 나오면 자동 갱신을 재개합니다.", AutomaticUpdateHold.Settling => "상태가 안정되면 자동 갱신을 재개합니다.", _ => null
        };
        model.SetAutomaticUpdateState(activityPolicy.AllowAutomaticUpdates, message);
    }
    public void Dispose()
    {
        lock (stateGate)
        {
            if (disposed) return;
            disposed = true;
            if (drawSubscribed) pluginInterface.UiBuilder.Draw -= Draw;
            if (mainSubscribed) pluginInterface.UiBuilder.OpenMainUi -= OpenMain;
            if (configSubscribed) pluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
            if (frameworkSubscribed) framework.Update -= Update;
            model?.DrainPendingActions(ex => log.Error(ex, "DuckbillCalendar pending edit failed during unload"));
            foreach (var command in registeredCommands) commands.RemoveHandler(command);
            registeredCommands.Clear();
            if (main != null) main.IsOpen = false;
            windows.RemoveAllWindows();
            alerts?.Dispose();
            frontline?.Dispose();
            workspace?.Dispose();
        }
    }
}
