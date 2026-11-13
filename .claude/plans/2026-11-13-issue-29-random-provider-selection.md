# Issue #29 — Support `--bg random:<provider>` and configurable provider order

Issue: https://github.com/ardalis/ImgForge/issues/29
Parent: #23. Builds on #24 (`IRandomBackgroundProvider`), #25 (ordered fallback), #26 (LoremFlickr), #28 (placehold.co).

## Summary

Let users pick a random background provider from the CLI: `--bg random` keeps the default fallback order, and
`--bg random:<provider>[:<option>]` uses one specific provider.

## Decisions / assumptions

- **Syntax:** `random` | `random:<provider>` | `random:<provider>:<option>`. Case-insensitive, surrounding whitespace
  ignored (matching the existing `random` check).
- **Provider names:** `picsum`, `loremflickr`, `placeholder`. Short names keep the CLI easy to type. Host names
  (`picsum.photos`) aren't accepted, so there's one way to spell each provider.
- **Options:** `loremflickr:<keyword>` (e.g. `random:loremflickr:technology` or `city,night`, using the existing
  keyword rules); `placeholder:<color>` (6-digit hex, `#` optional). `picsum` takes no option; giving one is an error.
- **Single provider = no fallback.** If someone names a provider, they asked for that provider specifically, so
  silently swapping in another would be surprising. On failure the error names the provider and suggests plain
  `--bg random`, which tries every provider in turn.
- **Default order unchanged:** picsum.photos → placehold.co. LoremFlickr is selectable but **not** in the default
  order: re-checked 2026-11-13 and `loremflickr.com/1280/720/buildings` still returns HTTP 500 (see the comment on
  #29). If someone picks it explicitly while it's down, they get a clear error naming the HTTP status.
  The owner's comment on #29 said to re-check LoremFlickr before making it selectable. I re-checked, and it still fails.
  I'm keeping it selectable anyway, because picking it is an explicit opt-in, the code already exists (#26), and when
  it recovers no code change is needed. To reduce surprise, the help text and docs call it unreliable and never use it
  as the example command. The PR description flags this so the owner can veto it; removing it is a one-line change in
  `RandomBackgroundSpec.ProviderNames`.
- **Errors:** an unknown provider, a missing provider name (`random:`), an empty option, an option on a provider that
  takes none, or an invalid option all produce one clear `ArgumentException`. Unknown provider names list the valid
  ones. The CLI checks `--bg` **before** resolving the template or prompting for a format, so a typo fails
  immediately.
- **Not done here (record):** a user-defined fallback list (e.g. `random:picsum,placeholder`). The issue's proposed
  syntax and tasks only cover default vs. one provider, and commas are already used in LoremFlickr keywords. Can be a
  follow-up if wanted.
- **Edge:** a relative local file literally named `random:...` is now read as a provider spec. Such paths can't exist
  on Windows. On Linux and macOS, `./random:x.jpg` still works.
- **#27 (random.imagecdn.app):** checked and not implemented. It only proxies LoremFlickr (HTTP 500) and LoremPixel
  (HTTP 500), and the other endpoints hung. Findings are posted on #27.

## Phase 1: Parsing and provider selection (Core)

- [x] `RandomBackgroundSpec` (in `RandomBackgrounds/`): `IsRandom(string?)` and `Parse(string)`, which return the
      provider selection or throw `ArgumentException` with a user-facing message
- [x] `CreateResolver()`: the default resolver for plain `random`, or a single-provider resolver configured with the
      option
- [x] Static `IsValidKeyword` / `IsValidColor` helpers on the LoremFlickr and placeholder providers, shared by their
      setters and the parser
- [x] `TemplateRenderer.IsRandomBackground` delegates to `RandomBackgroundSpec.IsRandom` (so the render guard also
      covers `random:x`)
- [x] `ImageGenerator` uses the spec's resolver (the default resolver stays injectable for tests)
- [x] `RandomBackgroundResolver` error message names the actual `--bg` value. For a single provider, it suggests
      `--bg random`

## Phase 2: CLI

- [x] `GenerateCommand` validates `--bg` up front, before template resolution or the format prompt
- [x] Update the `--bg` help text (provider names, options, default order)
- [x] `ImgForge.Core` `InternalsVisibleTo("ImgForge")` so the CLI can use the internal parser

## Phase 3: Automated tests (`tests/ImgForge.Tests`, unit)

- [x] `RandomBackgroundSpecTests`: `IsRandom` truth table; parses each provider with and without an option; case
      and whitespace handling; errors for unknown provider (lists valid names), empty name, empty option, option on
      picsum, invalid keyword, and invalid color
- [x] `CreateProvider` / `ImageGenerator.ResolverFor`: default uses the configured resolver; a named provider gets a
      single-provider resolver with the keyword or color, the `--bg` text, and the configured timeout and temp dir
- [x] Resolver error message for a single named provider includes the `--bg` value and the `--bg random` tip
- [x] `TemplateRenderer` guard covers `random:picsum`
- [x] `GenerateCommand`: an invalid `--bg random:bogus` fails with exit code 1 and the error, without prompting
      (`GenerateCommandBackgroundTests`, invoking the real command with console capture; added after review)
- [x] `--bg` help text mentions the provider syntax and names (added after review)
- [x] Existing tests still pass

## Phase 4: Docs

- [x] README options table, inline example comments, and examples
- [x] Docs site (`docs/content/docs/...`) where `--bg random` is described

## Phase 5: Independent review

- [x] A separate agent with fresh context reviews the full diff against #29 (correctness, missed requirements, test
      gaps, regressions, security, performance, accessibility)
- [x] Address all significant findings and record skipped ones below

## Phase 6: Final verification

- [x] `dotnet BuildTestFormat.cs` passes (build, all tests, `dotnet format --verify-no-changes`)

## Review notes

A separate general-purpose agent with fresh context reviewed `git diff origin/main...HEAD`. It found no high-severity
issues. It confirmed that user input can't inject anything into the provider URLs, that plain `random`, local files,
and URLs are unchanged, and that build and tests pass. Findings:

1. **(Medium) LoremFlickr selectable despite the owner's re-check gate, and used as the example:** addressed. The
   examples now use `random:placeholder:0f172a`. The help text and docs say loremflickr.com is unreliable. The decision
   to keep it selectable is recorded above and flagged in the PR.
2. **(Medium) No test that the CLI validates `--bg` before the template and the format prompt, and no help-text test:**
   addressed with `GenerateCommandBackgroundTests`. It runs `generate` with no format and a missing template, and
   asserts exit code 1, the exact error on stderr, and no prompt on stdout. It runs in a non-parallel collection
   because it swaps `Console` streams.
3. **(Low) `random:picsum:` said "the picsum option ... is empty" with a trailing space:** fixed. Any option on picsum,
   empty or not, now gives "picsum doesn't take an option". Test added.
4. **(Low) Template-variable docs said `random:<provider>` falls back from picsum to placehold.co:** fixed in README and
   `templates.md`.
5. **(Low) The docs fallback/warning paragraph read as if it applied to named providers:** fixed. It now starts "With
   plain `random`".
6. **(Low) `random :picsum` isn't recognized, and a Unix file named `random:x` needs `./`:** no code change. Docs now
   mention the `./random:...` prefix. `random :picsum` falls through to the normal "file not found" path, which is
   acceptable for a typo.
