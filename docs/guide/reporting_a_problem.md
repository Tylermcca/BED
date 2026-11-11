# Reporting a problem

Open **Help > Report a problem**. You can reach Help with Alt+H, then choose Report a problem with the keyboard. The start screen also has a Report a problem button.

Write what happened and what you expected. The checklist lets you include the BED version, operating system version, project kind, and the last 200 lines of BED's own diagnostic log. Game output and project contents are excluded from the text. The preview shows the exact text used by Copy report and Save report. Absolute paths, your user folder, and the current project's name are removed before the preview appears. Redaction can remove the rest of a line containing a path; rewrite that sentence without its path if needed.

**Copy report** puts the preview text on the clipboard. **Save report** writes a new Markdown report in the existing folder you choose or type. It never replaces an older report. Nothing is written until you press Save report.

The optional **Include current-window picture** checkbox starts off. Checking it captures the current BED window locally when the host supports that. Controls containing known private paths or the current project identity are covered. Images can still contain private game content: open and review the saved picture before sharing it. A picture is saved beside the Markdown report; it is never copied to the issue page or uploaded by BED.

**Open the issue page** opens the public repository's new-issue form in your browser with the report title and text filled in. You review and submit it there. Long reports are shortened to fit a bounded browser address; use Copy report to paste the complete text into the issue. Attach the saved picture yourself if you want it included. BED sends no telemetry.

For a text report without opening BED's window, run `bed-cli report`. This prints the same report template to standard output. It opens no project, writes no report file, and launches no browser.

BED keeps its own diagnostic events in the logs subfolder of personal settings, with two log files of at most 1 MiB each. Entries have a timestamp, level and source area. They contain fixed BED event names, with no document contents, field values, clipboard text, AI-assistant traffic or game output. The CLI can read the same saved log without contacting a running window. Headless screenshot commands do not write this log or add diagnostic files to the settings folder.

The optional `reportRepositoryUrl` key in your personal settings selects the repository. It defaults to the public BED repository; an invalid address falls back to that default with a plain note. It must be an HTTPS repository address without credentials, a query or a fragment.
