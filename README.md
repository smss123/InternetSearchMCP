# InternetSearchMCP

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/Model%20Context%20Protocol-1.2.0-blue)](https://modelcontextprotocol.io/)
[![GitHub](https://img.shields.io/badge/GitHub-smss123%2FInternetSearchMCP-181717?logo=github)](https://github.com/smss123/InternetSearchMCP)
[![Last commit](https://img.shields.io/github/last-commit/smss123/InternetSearchMCP)](https://github.com/smss123/InternetSearchMCP/commits/main)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> **Repository:** [github.com/smss123/InternetSearchMCP](https://github.com/smss123/InternetSearchMCP)

An MCP (Model Context Protocol) server that gives any MCP-capable AI client — Claude Code, Claude Desktop, Rider AI Assistant, Cursor, etc. — the ability to search the live web and read pages. Built with .NET 10 and the official [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol) C# SDK, using DuckDuckGo as the search backend (no API key required).

## Tools

| Tool | What it does | When to use |
|------|--------------|-------------|
| `enhance_prompt` | Rewrites the raw user prompt into a structured, context-enriched version and recommends which tool to call next | **Always, first** — before answering or calling other tools |
| `smart_search` | One-shot search: fetches the top result pages in parallel, ranks their content against the query, returns a consolidated answer block with per-source attribution | Default choice for general questions |
| `xprema_code` | Searches for a coding problem/error and returns **only code snippets** (formatting preserved), each with a one-line source URL | Compiler errors, exceptions, API usage, how-to-implement |
| `search_internet` | Low-level: returns a list of candidate result links (title, URL, snippet) | When you want to pick pages manually |
| `fetch_page_content` | Low-level: fetches one URL and returns cleaned plain text | Reading a specific page in full |

All tools work in any language, including Arabic (search and relevance ranking are Unicode-aware).

### Tool parameters

**`enhance_prompt`**
- `rawPrompt` (string, required) — the user's raw, unmodified prompt (any language)

Returns an `ENHANCED PROMPT:` block (task statement + the original wording quoted verbatim + expected output format), a `RECOMMENDED NEXT TOOL:` line, and — for vague follow-ups like *"and how do I fix it?"* — a `CONTEXT USED:` line showing which recent session topics were attached. It never translates: the LLM is instructed to respond in the same language as the original prompt, so every language works.

> **Making clients call it every time:** MCP cannot force tool order, so add this to your client's system prompt (especially for local models in LM Studio):
> *"Always call enhance_prompt first with the user's raw message, then follow its ENHANCED PROMPT and RECOMMENDED NEXT TOOL."*

**`smart_search`**
- `query` (string, required) — what to look up
- `maxSources` (int, 1–5, default 3) — how many top pages to read and consolidate

**`xprema_code`**
- `problem` (string, required) — the error message or coding question
- `language` (string, optional) — language/framework hint, e.g. `"C#"`, `"python"`, `"ABP Framework"`
- `maxSources` (int, 1–5, default 3)

**`search_internet`**
- `query` (string, required)

**`fetch_page_content`**
- `url` (string, required) — absolute http/https URL

## Installation

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Build

```bash
git clone https://github.com/smss123/InternetSearchMCP.git
cd InternetSearchMCP
dotnet publish InternetSearchMCP -c Release
```

Note the publish output path printed at the end, e.g.:

```
InternetSearchMCP/bin/Release/net10.0/<rid>/publish/InternetSearchMCP.dll
```

where `<rid>` is your platform (`osx-arm64`, `win-x64`, `linux-x64`, …). You can also run straight from the build output without publishing.

### Register with Claude Code

```bash
claude mcp add internet-search -- dotnet /ABSOLUTE/PATH/TO/publish/InternetSearchMCP.dll
```

### Register with Claude Desktop

Add to `claude_desktop_config.json` (macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "internet-search": {
      "command": "dotnet",
      "args": ["/ABSOLUTE/PATH/TO/publish/InternetSearchMCP.dll"]
    }
  }
}
```

### Register with LM Studio

LM Studio (0.3.17+) supports MCP servers. Open **Program** (right sidebar) → **Install → Edit mcp.json**, and add:

```json
{
  "mcpServers": {
    "internet-search": {
      "command": "dotnet",
      "args": ["/ABSOLUTE/PATH/TO/publish/InternetSearchMCP.dll"]
    }
  }
}
```

Save, then toggle the server on in the Program panel. Any tool-capable model you run locally (Qwen, Llama, Mistral, etc.) can now call `smart_search` and `xprema_code`. Tip: smaller local models follow tools better if your system prompt says *"Use the smart_search tool for questions needing current information; use xprema_code for coding errors."*

### Register with Cursor

**Settings → MCP → Add new global MCP server**, or create `~/.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "internet-search": {
      "command": "dotnet",
      "args": ["/ABSOLUTE/PATH/TO/publish/InternetSearchMCP.dll"]
    }
  }
}
```

### Register with VS Code (GitHub Copilot)

Create `.vscode/mcp.json` in your workspace (or add via **MCP: Add Server** from the command palette):

```json
{
  "servers": {
    "internet-search": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["/ABSOLUTE/PATH/TO/publish/InternetSearchMCP.dll"]
    }
  }
}
```

### Register with JetBrains Rider / IntelliJ (AI Assistant)

**Settings → Tools → AI Assistant → Model Context Protocol (MCP) → Add**, choose *As JSON* and paste the same `command`/`args` block used above.

### Other MCP clients

Any client that speaks MCP over **stdio** works the same way — the launch command is always:

```
dotnet /ABSOLUTE/PATH/TO/publish/InternetSearchMCP.dll
```

For clients that only support HTTP/SSE transports, put the server behind an MCP stdio-to-HTTP gateway (e.g. `mcp-proxy`).

Restart the client after editing its config. The server communicates over stdio; all logs go to stderr so they never corrupt the protocol stream.

## Usage

Once registered, just ask your AI client questions that need live web data — it will pick the right tool automatically. Examples:

- *"What are the latest SPHERE humanitarian standards?"* → `smart_search`
- *"ما هي معايير اسفير الإنسانية؟"* → `smart_search` (Arabic works end-to-end)
- *"Fix: NullReferenceException when calling HttpClient.PostAsync"* → `xprema_code` returns ranked code snippets only, each preceded by `// Source: <url>`
- *"Read this page for me: https://example.com/article"* → `fetch_page_content`

### Behavior notes

- **Timeouts & limits**: each page fetch has a 10-second timeout and a ~1 MB read cap; total output is bounded (~8,000 characters), so responses stay token-friendly.
- **Graceful degradation**: if none of the top pages can be read (or contain no code, for `xprema_code`), the tools return the raw result list (titles/URLs/snippets) so the client can fall back to the low-level tools instead of failing.
- **No API keys**: uses DuckDuckGo's public HTML endpoint. Heavy automated use may be rate-limited — this server is intended for interactive assistant workloads.

## Project structure

```
InternetSearchMCP/
├── Program.cs                          # Host setup + MCP tool registration (stdio transport)
└── Tools/
    ├── RandomNumberTools.cs            # Sample tool from the template
    └── SearchEngines/
        ├── DuckDuckGoClient.cs         # Shared core: search, organic-result parsing,
        │                               #   page fetch (timeout/size caps), HTML→text,
        │                               #   code-block extraction
        ├── SmartSearchTool.cs          # smart_search
        ├── XpremaCodeTool.cs           # xprema_code
        └── DuckDuckGoSearchTool.cs     # search_internet + fetch_page_content
```

## Development

```bash
dotnet build InternetSearchMCP -c Release     # build
```

Quick manual smoke test over stdio (initialize, then call a tool):

```bash
( echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"1.0"}}}'
  echo '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  echo '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"smart_search","arguments":{"query":"test"}}}'
  sleep 20 ) | dotnet path/to/InternetSearchMCP.dll
```

Specs and change history live under `openspec/` (OpenSpec spec-driven workflow).
