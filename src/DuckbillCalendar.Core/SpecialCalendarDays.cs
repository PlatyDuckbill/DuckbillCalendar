namespace DuckbillCalendar.Core;

public static class SpecialCalendarDays
{
    public static bool IsOguDay(DateOnly date) => date.Month == 5 && date.Day == 9;
}
