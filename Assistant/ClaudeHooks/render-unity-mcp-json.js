#!/usr/bin/env node
/*
 * PostToolUse hook: render Unity MCP tool results as readable text instead of an escaped JSON blob.
 *
 * Why: the Unity MCP bridge requires tool results to be an object, and the transport JSON-encodes it, so
 * every newline arrives as a literal \n. Claude Code's CLI pretty-prints the JSON but does not unescape
 * string values (anthropics/claude-code#21186, closed as not planned), so anything written to be read as
 * text renders as one long escaped ribbon. No hook surface applies markdown either — the tool-result panel
 * and systemMessage both render plain — so plain text with real newlines is the whole available target.
 *
 * Payload shape, confirmed by probing a live call — the envelope sits TWO layers down, not one:
 *   tool_response = [ { type: "text", text: "{\n  \"success\": true, ... }" } ]
 * i.e. an MCP content-block array, whose text is the Unity JSON. Assuming tool_response was the envelope
 * itself made an earlier version of this hook fire and silently decline on every call.
 *
 * ONE rule, applied to every Unity tool: walk the JSON, one line per leaf, multi-line strings written out
 * as indented text. No per-tool branches, no eliding, no summarising. Earlier revisions special-cased
 * Unity_RunCommand's source echo and short-circuited the log tools' already-formatted reports; both were
 * dropped deliberately. A rewrite layer that quietly decides what matters is one that can quietly lose
 * something, and the per-tool knowledge belongs in the tools, not here.
 *
 * updatedToolOutput REPLACES the tool result and the original is not preserved anywhere, so every path
 * fails open: on any unexpected shape, parse failure, or thrown error it prints nothing and the original
 * passes through untouched. Printing nothing is always the safe outcome.
 *
 * Only fires for SUCCEEDING calls: failures route to PostToolUseFailure, which invokes hooks but ignores
 * updatedToolOutput, so a failed Unity call always shows the raw envelope. Nothing to be done about that
 * here.
 */

/** Dig the Unity envelope object out of whatever PostToolUse handed us. Returns null to leave it alone. */
function extractEnvelope(toolResponse) {
  let candidate = toolResponse;

  // MCP content-block array. Only the single-text-block case — anything else (images, multiple blocks)
  // is structure this hook has no business touching.
  if (Array.isArray(candidate)) {
    if (candidate.length !== 1) return null;
    const block = candidate[0];
    if (!block || block.type !== "text" || typeof block.text !== "string") return null;
    candidate = block.text;
  }

  if (typeof candidate === "string") {
    const trimmed = candidate.trim();
    if (!trimmed.startsWith("{")) return null; // not an envelope
    try {
      candidate = JSON.parse(trimmed);
    } catch {
      return null;
    }
  }

  if (!candidate || typeof candidate !== "object" || Array.isArray(candidate)) return null;
  return candidate;
}

const lf = (s) => String(s).replace(/\r\n/g, "\n").replace(/\s+$/, "");
const isMultiline = (s) => /[\r\n]/.test(s);

/**
 * One line per leaf, with multi-line strings written out as actual indented text rather than escaped onto
 * one line. Purely mechanical — nothing is dropped, reordered or summarised — so it is safe to point at
 * payloads whose shape nobody has looked at yet.
 *
 * Nesting is indentation, not dotted paths: `a.b.c.d` / `a.b.c.e` rewrites the whole prefix on every leaf,
 * which on a record tree is most of the output. Each segment is written once instead.
 */
function emit(key, value, depth, out) {
  const pad = "  ".repeat(depth);

  if (value === null) {
    out.push(`${pad}${key}: null`);
    return;
  }

  if (Array.isArray(value)) {
    if (value.length === 0) out.push(`${pad}${key}: []`);
    // The element keeps this depth — its own children are what indent.
    else value.forEach((v, i) => emit(`${key}[${i}]`, v, depth, out));
    return;
  }

  if (typeof value === "object") {
    const keys = Object.keys(value);
    if (keys.length === 0) {
      out.push(`${pad}${key}: {}`);
      return;
    }

    out.push(`${pad}${key}:`);
    for (const k of keys) emit(k, value[k], depth + 1, out);
    return;
  }

  if (typeof value === "string") {
    if (value.trim() === "") {
      out.push(`${pad}${key}: (empty)`);
      return;
    }

    if (isMultiline(value)) {
      out.push(`${pad}${key}:`);
      for (const line of lf(value).split("\n")) out.push(pad + "  " + line);
      return;
    }

    out.push(`${pad}${key}: ${value}`);
    return;
  }

  out.push(`${pad}${key}: ${value}`); // number, boolean
}

let raw = "";
process.stdin.on("data", (chunk) => (raw += chunk));
process.stdin.on("end", () => {
  try {
    const payload = JSON.parse(raw);
    const envelope = extractEnvelope(payload.tool_response ?? payload.toolResponse);
    if (!envelope) return;

    const out = [];
    for (const k of Object.keys(envelope)) emit(k, envelope[k], 0, out);
    const text = out.join("\n");

    // Nothing gained if the result was a single short line to begin with.
    if (!text || !isMultiline(text)) return;

    process.stdout.write(
      JSON.stringify({
        hookSpecificOutput: { hookEventName: "PostToolUse", updatedToolOutput: text },
      })
    );
  } catch {
    // Fail open — emit nothing, original result survives.
  }
});
