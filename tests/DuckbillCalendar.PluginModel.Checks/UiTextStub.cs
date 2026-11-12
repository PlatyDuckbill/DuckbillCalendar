namespace DuckbillCalendar.Plugin;

// This harness compiles the actual model source without game/UI dependencies. Only localization
// culture selection is substituted; resource parity and real ResourceManager loading are checked separately.
internal static class UiText
{
    public static void SetLanguage(string _) { }
}
