# InternetSearchMCP

An MCP (Model Context Protocol) server that gives any MCP-capable AI client — Claude Code, Claude Desktop, Rider AI Assistant, Cursor, etc. — the ability to search the live web and read pages. Built with .NET 10 and the official [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol) C# SDK, using DuckDuckGo as the search backend (no API key required).

## Tools

| Tool | What it does | When to use |
|------|--------------|-------------|
| `smart_search` | One-shot search: fetches the top result pages in parallel, ranks their content against the query, returns a consolidated answer block with per-source attribution | Default choice for general questions |
| `xprema_code` | Searches for a coding problem/error and returns **only code snippets** (formatting preserved), each with a one-line source URL | Compiler errors, exceptions, API usage, how-to-implement |
| `search_internet` | Low-level: returns a list of candidate result links (title, URL, snippet) | When you want to pick pages manually |
| `fetch_page_content` | Low-level: fetches one URL and returns cleaned plain text | Reading a specific page in full |

All tools work in any language, including Arabic (search and relevance ranking are Unicode-aware).

### Tool parameters

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
git clone <this-repo>
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

Restart the client afterwards. The server communicates over stdio; all logs go to stderr so they never corrupt the protocol stream.

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
