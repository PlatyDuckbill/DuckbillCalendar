using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DuckbillCalendar.Infrastructure;
using System.Globalization;

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
    private readonly List<string> registeredCommands = [];
    private readonly CalendarWorkspace workspace = null!;
    private readonly CalendarModel model = null!;
    private readonly LocalAlertCoordinator alerts = null!;
    private readonly MainWindow main = null!;
    private readonly CompactWindow compact = null!;
    private bool drawSubscribed;
    private bool mainSubscribed;
    private bool configSubscribed;
    private bool frameworkSubscribed;
    private bool disposed;
    private DateTimeOffset nextErrorLog;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager, IFramework framework,
        IPluginLog log, IChatGui chat, IClientState clientState, IPlayerState playerState, ICondition conditions)
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
            alerts = new LocalAlertCoordinator(workspace);
            UiText.SetLanguage(workspace.State.View.Language);
            main = new MainWindow(model);
            compact = new CompactWindow(model);
            main.ToggleCompact = ToggleCompact;
            compact.OpenMain = () => main.IsOpen = true;
            windows.AddWindow(main);
            windows.AddWindow(compact);
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
            model.Tick(DateTimeOffset.UtcNow);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OnCommand(string command, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "compact": case "small": ToggleCompact(); break;
            case "settings": main.OpenSettings(); break;
            default: main.IsOpen = !main.IsOpen; break;
        }
    }
    private void OpenMain() => main.IsOpen = true;
    private void OpenSettings() => main.OpenSettings();
    private void ToggleCompact() => compact.IsOpen = !compact.IsOpen;
    private void Draw()
    {
        if (disposed || clientState.IsPvP) return;
        using var theme = new ThemeScope(model.State.View.Theme);
        windows.Draw();
    }
    private void Update(IFramework _)
    {
        if (disposed) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            model.Tick(now);
            var session = clientState.IsLoggedIn && playerState.IsLoaded && playerState.ContentId != 0
                ? playerState.ContentId.ToString(CultureInfo.InvariantCulture) : null;
            var canDeliver = !clientState.IsPvP && !conditions[ConditionFlag.InCombat] &&
                !conditions[ConditionFlag.BoundByDuty] && !conditions[ConditionFlag.BoundByDuty56] &&
                !conditions[ConditionFlag.BoundByDuty95] && !conditions[ConditionFlag.BetweenAreas] &&
                !conditions[ConditionFlag.BetweenAreas51] && !conditions[ConditionFlag.OccupiedInCutSceneEvent] &&
                !conditions[ConditionFlag.WatchingCutscene] && !conditions[ConditionFlag.WatchingCutscene78];
            alerts.Tick(now, session, canDeliver, alert => chat.Print("[DuckbillCalendar] " +
                UiText.Get("LocalAlertMessage", alert.Title.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '), UiText.Time(alert.BoundaryAt))));
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
    public void Dispose()
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
        if (compact != null) compact.IsOpen = false;
        windows.RemoveAllWindows();
        alerts?.Dispose();
        workspace?.Dispose();
    }
}
