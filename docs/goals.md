# Goals

What BED is for, in order. When two goals conflict, the earlier one wins, inside three rules no goal can bend: every edit is checked and can be undone, content rights and licences are respected, and the editor never depends on any one game.

1. **Easy to use.** A simple surface over full depth. You can open a project with no setup, find any command by typing its name (Ctrl+Shift+P), and every error says what went wrong, where, and what to do next.
2. **Powerful when needed.** Tables, forms, links between files, bulk edits with a preview, checks and a command line, for when a project gets big.
3. **Expandable.** A game adds its own rules, panels, checks and a Play button through a small C# adapter. See [Writing an adapter](guide/writing_an_adapter.md).
4. **Edits are safe.** Every change is validated before it lands, is one undo step, and is recorded. BED won't save over a file another program changed.
5. **Data lasts.** Files are plain JSON with a stable format: the same data always saves to the same bytes.
6. **Games ship only what they need.** A game reads its own data; it never needs BED to run.
7. **Any BRUTAL game can use it.** Nothing about a particular game is built into the editor; games plug in through the same extension points the samples use.
8. **Room for AI.** An AI assistant can use BED only through the same checked commands a person uses (`bed-cli.exe`), and only proposes changes you accept.
9. **Ready to give away.** MIT licensed, with original samples and a full record of third-party licences.
10. **Fits BRUTAL.** Built on BRUTAL's own packages, at the versions BRUTAL games use.
11. **Small steps.** Each change is small, tested and usable on its own.
12. **Usable by everyone.** Every action works from the keyboard, text can be made larger, and the command line serves screen-reader users.
13. **Play from the editor.** With an adapter, start your game with your unsaved changes in it, inside BED or in its own window.

Later goals include lighting and animation previews, switchable rendering styles and live code changes.
