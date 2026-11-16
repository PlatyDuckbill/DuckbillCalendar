using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Infrastructure;

public sealed record CalendarBackup
{
    [JsonRequired] public string Format { get; init; } = "DuckbillCalendar.Backup";
    [JsonRequired] public int FormatVersion { get; init; } = 1;
    [JsonRequired] public DateTimeOffset CreatedAt { get; init; }
    [JsonRequired] public LocalState State { get; init; } = new();
}
public sealed record CalendarTransferPreview(bool IsBackup, int Entries, int Duplicates, int Unsupported, int Profiles, int Categories, IReadOnlyList<PersonalEntry> Sample);

public sealed partial class CalendarWorkspace
{
    private static readonly UTF8Encoding TransferEncoding = new(false, true);
    private LocalState? transferState;
    private long transferVersion;
    private int transferRequest;
    public CalendarTransferPreview? TransferPreview { get; private set; }
    public string TransferCode { get; private set; } = "Ready";
    public string? RecoveryBackupPath { get; private set; }
    public long RestoreGeneration { get; private set; }

    public void CancelTransfer()
    {
        transferRequest++; transferState = null; TransferPreview = null; TransferCode = "Ready";
    }

    private void Transfer(Func<CancellationToken, Task> action)
    {
        var request = ++transferRequest;
        TransferCode = "Working";
        Enqueue(async ct =>
        {
            var code = "Done";
            try { await action(ct); }
            catch (OperationCanceledException) { code = "Cancelled"; }
            catch (InvalidDataException ex) { code = ex.Message.StartsWith("Calendar", StringComparison.Ordinal) ? ex.Message : "InvalidFile"; }
            catch (JsonException) { code = "InvalidFile"; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { code = "FileFailed"; }
            catch (Exception) { code = "InvalidFile"; }
            completions.Enqueue(() => { if (request == transferRequest && TransferCode == "Working") TransferCode = code; });
            return "TransferFinished";
        }, "TransferFailed");
    }

    public void ExportCalendar(string path, bool backup)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (TransferCode == "Working") return;
        var snapshot = State; var now = utcNow();
        Transfer(async ct =>
        {
            var bytes = backup ? JsonSerializer.SerializeToUtf8Bytes(new CalendarBackup { CreatedAt = now, State = snapshot }, CalendarJson.Options)
                : TransferEncoding.GetBytes(CalendarInterchange.Export(snapshot, now));
            if (bytes.Length > AtomicJsonStore.MaximumFileBytes) throw new InvalidDataException("FileTooLarge");
            // Explicitly chosen new files only: never overwrite an existing file or plugin state.
            if (!Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(backup ? ".json" : ".ics", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("CalendarFileExtension");
            var directory = Path.GetDirectoryName(path)!;
            var temporary = Path.Combine(directory, ".duckbill-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, ct);
                ct.ThrowIfCancellationRequested(); File.Move(temporary, path, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        });
    }

    public void PreviewCalendarFile(string path)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (TransferCode == "Working") return;
        transferState = null; TransferPreview = null;
        var snapshot = State; var version = stateVersion; var request = transferRequest + 1;
        Transfer(ct =>
        {
            ct.ThrowIfCancellationRequested();
            var bytes = AtomicJsonStore.ReadBounded(path);
            LocalState next; CalendarTransferPreview preview;
            if (Path.GetExtension(path).Equals(".ics", StringComparison.OrdinalIgnoreCase))
            {
                var import = CalendarInterchange.Import(TransferEncoding.GetString(bytes), snapshot);
                next = snapshot with
                {
                    Entries = snapshot.Entries.Concat(import.Entries).ToArray(),
                    Notifications = snapshot.Notifications with { Schedules = snapshot.Notifications.Schedules.Concat(import.Entries.Select(e =>
                        new ScheduleNotificationRule { ProfileId = snapshot.ActiveProfileId, EventId = e.Id, PhaseId = "local", Mode = "off" })).ToArray() }
                };
                preview = new(false, import.Entries.Count, import.Duplicates, import.Unsupported, 1, 0, import.Entries.Take(5).ToArray());
            }
            else if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                var backup = CalendarJson.Parse<CalendarBackup>(bytes);
                if (backup.Format != "DuckbillCalendar.Backup" || backup.FormatVersion != 1 || backup.CreatedAt == default) throw new InvalidDataException("InvalidBackupFormat");
                // The envelope's nested record defaults must not turn a truncated state into a valid empty backup.
                using var document = JsonDocument.Parse(bytes);
                next = CalendarJson.Parse<LocalState>(TransferEncoding.GetBytes(document.RootElement.GetProperty("state").GetRawText()));
                preview = new(true, next.Entries.Count, 0, 0, next.Profiles.Count, next.View.UserCategories.Count, next.Entries.Take(5).ToArray());
            }
            else throw new InvalidDataException("CalendarFileExtension");
            ThrowErrors(FeedValidator.ValidateLocal(next));
            // Check the eventual serialized size before allowing an in-memory state that cannot be saved.
            if (JsonSerializer.SerializeToUtf8Bytes(next, CalendarJson.Options).Length > AtomicJsonStore.MaximumFileBytes) throw new InvalidDataException("FileTooLarge");
            completions.Enqueue(() =>
            {
                if (request != transferRequest) return;
                if (version != stateVersion) { TransferCode = "StateChanged"; return; }
                transferState = next; transferVersion = version; TransferPreview = preview;
            });
            return Task.CompletedTask;
        });
    }

    public void ApplyCalendarFile()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (TransferCode == "Working" || transferState is not { } next || TransferPreview is not { } preview) return;
        if (transferVersion != stateVersion) { CancelTransfer(); TransferCode = "StateChanged"; return; }
        var original = State; var version = stateVersion; var request = transferRequest + 1;
        transferState = null; TransferPreview = null;
        Transfer(async ct =>
        {
            var name = "state.before-transfer-" + utcNow().ToString("yyyyMMddHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N") + ".json";
            await store.SaveAsync(name, new CalendarBackup { CreatedAt = utcNow(), State = original }, ct);
            completions.Enqueue(() =>
            {
                if (disposed || request != transferRequest) return;
                if (version != stateVersion) { TransferCode = "StateChanged"; return; }
                RecoveryBackupPath = store.Resolve(name);
                // Historical pending chat must not be replayed by restoring an old inbox.
                var restored = preview.IsBackup ? next with { NotificationState = next.NotificationState with
                { Inbox = next.NotificationState.Inbox.Select(n => n with { ChatState = "none" }).ToArray() } } : next;
                UpdateState(restored);
                if (preview.IsBackup) RestoreGeneration++;
                TransferCode = "Applied";
            });
        });
    }
}
