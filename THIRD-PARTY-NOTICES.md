# Third-party dependencies

The developer-only `CalendarDataImport` tool uses **AngleSharp 1.10.1**, pinned in its project and package lock.

- Copyright 2013–2026, AngleSharp
- License: MIT, as declared in the NuGet package metadata
- Source: https://github.com/AngleSharp/AngleSharp
- Package source revision: `d3cd1a023256b06497bc0cf803702fe94661f358`
- Package license: https://licenses.nuget.org/MIT
- Purpose: parse saved public-event HTML into unreviewed local candidates. No scripts or external resources are executed by the parser.

AngleSharp is not referenced by or bundled with the game plugin. Build-time Dalamud SDK/Packager and the installed Dalamud runtime retain their own licenses. Runtime reference DLLs are not copied into this repository or distributed by this project.

DuckbillFisher was consulted for architecture and API usage. Its implementation files and assets have not been imported into DuckbillCalendar. If code is transferred later, preserve its AGPL-3.0 and third-party conditions.
