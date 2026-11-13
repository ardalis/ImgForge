# Issue #39 — Add usage examples to CLI help output

Issue: https://github.com/ardalis/ImgForge/issues/39

## Summary

"Add examples like the ones in README.md. Also make sure every supported switch is covered by the --help output."

`imgforge generate --help` already lists all 13 options, but has no examples, and `--width`/`--height` show
`[default: 1200]`/`[default: 630]` without saying that, with no size flags, ImgForge prompts for a format instead.

## Decisions / assumptions

- Add an `Examples:` section after the options in `generate --help`, built from a list of example command lines in
  `GenerateCommand` (based on README "Quick reference commands" / "Example Usage": built-in templates, random
  backgrounds, `random:<provider>`, headshot + filter, `--var`, overlays, `--out-dir`, explicit width/height).
- Root `imgforge --help` gets a short Examples section too, pointing to `imgforge generate --help`.
- Help shows the tool name `imgforge` (the installed command), not the assembly name `ImgForge`.
- Examples use `imgforge ...` form (global tool); the DNX form is a README concern.
- Examples are single-line (help is plain text; `\` continuations are shell-specific).
- "Every switch covered" is enforced by a test: every option on the command has a description and appears in the
  help text, and every example uses only real options and parses without errors.
- Clarify option descriptions where they lag the README: `--overlay` (passed to template as-is), `--headshot`
  (local path or URL), `--width`/`--height` (prompt / fallback behavior).

## Phase 1 — Help examples

- [x] `GenerateCommand.Examples` (internal list of command lines)
- [x] `Cli.BuildParser()` in `src/ImgForge/Cli.cs`: `CommandLineBuilder(root).UseDefaults().UseHelp(...)` appending
      an Examples section for `generate` and root; `Program.cs` uses it
- [x] Root command name `imgforge`
- [x] Improve option descriptions listed above

## Phase 2 — Tests (`tests/ImgForge.Tests/CliHelpTests.cs`)

- [x] `generate --help` output contains `Examples:` and every example
- [x] Every `generate` option has a non-empty description and its alias appears in help output
- [x] Every example parses with no errors and every option in `GenerateCommand` is used by at least one example
- [x] Root `--help` contains Examples and mentions `generate --help`

## Phase 3 — Docs

- [x] README: note that `imgforge generate --help` lists all options and examples

## Phase 4 — Independent review

- [ ] Separate agent reviews full diff (correctness, coverage, security, performance, accessibility)
- [ ] Address significant findings (or record reasons here)

## Phase 5 — Verification

- [ ] `dotnet BuildTestFormat.cs` clean
