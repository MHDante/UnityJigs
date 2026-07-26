# Claude Code hook: readable Unity MCP results

`render-unity-mcp-json.js` is a [Claude Code](https://code.claude.com) `PostToolUse` hook. It makes the
Unity MCP server's results legible to the agent instead of arriving as one escaped JSON ribbon.

Optional. Nothing in UnityJigs depends on it — the tools work fine without it, they just read badly.

## Why it exists

A Unity MCP tool returns an object. The transport JSON-encodes it, so every newline in every string
arrives as a literal `\n`. Claude Code's CLI pretty-prints the JSON envelope but does **not** unescape the
string values inside it, so a multi-line report renders as a single unreadable line
([anthropics/claude-code#21186](https://github.com/anthropics/claude-code/issues/21186), closed as not
planned).

This hurts most for `Unity.Logs` and `Unity.LogDetail` — whose entire payload is a formatted text report —
and for `Unity.RunCommand`, whose `executionLogs` and `localFixedCode` are multi-line by nature.

It cannot be fixed on the Unity side. The bridge requires tool results to be an object: returning a bare
string fails the call outright. No hook surface renders markdown either — the tool-result panel and
`systemMessage` are both plain text — so code fences and bold are wasted. Plain text with real newlines is
the entire available target, and rewriting the result is the only way to get there.

## What it does

One rule, applied to every `mcp__unity-mcp__*` tool: walk the JSON, one line per leaf, and write multi-line
strings out as indented text. Nesting is expressed by indentation rather than dotted paths, because
`a.b.c.d` / `a.b.c.e` rewrites the whole prefix on every leaf — on a record tree that is most of the
output.

```
success: true
message: Command executed successfully.
data:
  isCompilationSuccessful: true
  executionLogs:
    [Log] prefab lookup: found
    renderers: 86
```

**It is deliberately generic** — no per-tool branches, no eliding, no summarising. Earlier revisions
special-cased `RunCommand`'s source echo (it just repeats the submitted `Code`) and short-circuited the log
tools' already-formatted reports. Both were removed: a rewrite layer that decides what matters is one that
can quietly lose something, and per-tool knowledge belongs in the tools. If you are tempted to add a
special case here, add it to the tool instead.

Every path fails open. On an unexpected shape, a parse failure, or any thrown error it prints nothing and
the original result passes through untouched — which matters, because `updatedToolOutput` **replaces** the
tool result and the original is not preserved anywhere.

## Install

Copy the script out of the package rather than pointing at it in place. On a git package reference jigs
resolves to `Library/PackageCache/com.mhdante.unity-jigs@<hash>/`, and that hash changes on every update,
so a path into the cache breaks the next time you bump the version.

1. Copy `render-unity-mcp-json.js` to `~/.claude/hooks/` (`C:\Users\<you>\.claude\hooks\` on Windows).

2. Add to `~/.claude/settings.json`, merging with any existing `hooks` block:

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "mcp__unity-mcp__.*",
        "hooks": [
          {
            "type": "command",
            "command": "node ~/.claude/hooks/render-unity-mcp-json.js"
          }
        ]
      }
    ]
  }
}
```

   On Windows use a full forward-slash path — `node C:/Users/<you>/.claude/hooks/render-unity-mcp-json.js`.

3. Open `/hooks` once in Claude Code, or restart it, so the config reloads.

Requires `node` on `PATH`.

### The matcher is anchored

`"mcp__unity-mcp__"` matches **nothing** — hook matchers are full matches, not substrings. The `.*` is
required. A missing wildcard fails silently: the hook simply never runs.

## Verifying

Ask Claude to read the Unity console. Installed, the result is indented text. Not installed, it is a JSON
object full of `\n`. That difference is visible to the agent, so `Unity.Logs`' own description tells it to
point here when it sees the escaped form.

## Limitations

- **Failed tool calls are not covered.** They route to `PostToolUseFailure`, which does invoke hooks but
  ignores `updatedToolOutput` — it is a notification event. A failing Unity call always shows the raw
  envelope. Relevant in practice: `RunCommand` compile errors are numbered against `localFixedCode`, not
  the submitted `Code`, because Unity wraps the script in a namespace before compiling — reported lines sit
  **+2** from the source as written.
- **Unity only.** Other MCP servers are left alone. Blender's, for instance, returns record trees with no
  multi-line strings, so there is no escaping to undo and flattening would only make it longer.
- **Tool-call parameters are untouched.** The escaped `Code: "using System...\n..."` on the call line is the
  CLI rendering the request; no hook reaches it
  ([#31639](https://github.com/anthropics/claude-code/issues/31639)).

## Uninstall

Delete the `PostToolUse` entry from `~/.claude/settings.json` and remove the script. The Unity tools carry
on working; their output goes back to being escaped JSON.
