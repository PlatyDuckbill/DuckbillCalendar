/* Pure date layout shared by the month renderer. ISO local dates sort chronologically. */
(() => {
  const shiftDate = (day, offset) => new Date(Date.parse(day + 'T12:00:00Z') + offset * 86400000).toISOString().slice(0, 10);
  function layoutWeek(ranges, weekStart) {
    const weekEnd = shiftDate(weekStart, 6);
    const visible = ranges.filter(r => r.start <= weekEnd && r.end >= weekStart)
      .sort((a, b) => a.start.localeCompare(b.start) || b.end.localeCompare(a.end) || a.key.localeCompare(b.key));
    const occupied = [];
    return visible.map(range => {
      const first = range.start < weekStart ? weekStart : range.start;
      const last = range.end > weekEnd ? weekEnd : range.end;
      const startColumn = Math.round((Date.parse(first + 'T12:00:00Z') - Date.parse(weekStart + 'T12:00:00Z')) / 86400000);
      const endColumn = Math.round((Date.parse(last + 'T12:00:00Z') - Date.parse(weekStart + 'T12:00:00Z')) / 86400000);
      let lane = occupied.findIndex(end => end < startColumn);
      if (lane < 0) lane = occupied.length;
      occupied[lane] = endColumn;
      return {...range, startColumn, endColumn, lane, continuesBefore:range.start < weekStart, continuesAfter:range.end > weekEnd};
    });
  }
  window.CalendarLayout = {shiftDate, layoutWeek};
})();
