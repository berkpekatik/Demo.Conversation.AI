# Demo.Conversation.AI

Lightweight demo showing how to run a local llama-compatible model server and send chat-completion requests from a small C# console app.

## Files

- `LlamaService.cs` — HTTP client wrapper for the model server:
  - Field: `_client` (reused `HttpClient`).
  - Field: `_baseUrl` (server base URL).
  - `CheckHealthAsync()` — GET `{baseUrl}/v1/health`, returns raw JSON string (throws on non-success status).
  - `CreateChatCompletionAsync(string model, string userMessage)` — POST `{baseUrl}/v1/chat/completions` with a chat-style payload:
    ```json
    {
      "model": "local",
      "messages": [
        { "role": "system", "content": "Short answer" },
        { "role": "user", "content": "<userMessage>" }
      ]
    }
    ```
    Returns the raw JSON response from the server. The method currently uses `JsonSerializer.Serialize` and `HttpClient.PostAsync`.

- `Program.cs` — Console app that:
  - Locates and starts `llama-server.exe` from `llama.cpp` with a random port.
  - Uses `DelayWithCountdownAsync(int milliseconds)` to show a per-second in-place countdown while waiting for the server to be ready.
  - Creates `LlamaService` with the server URL and performs a health check.
  - Interactive loop: reads user input; if not `exit`, it calls `CreateChatCompletionAsync("local", prompt)` and prints the returned JSON.

## How to run

1. Ensure the `llama.cpp` folder and `llama-server.exe` are present under the application folder (the project starts `llama-server.exe`).
2. Build and run the console app:

```powershell
dotnet run
```

3. When prompted, type a message (for example `Selam`) and press Enter.

## Example outputs (samples captured from a run)

- `CheckHealthAsync()` returned:

```json
{"status":"ok"}
```

- `CreateChatCompletionAsync(...)` returned a chat-completion JSON similar to:

```json
{
  "choices": [
    {
      "finish_reason": "stop",
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "I understand you're looking for a short answer. Here's a concise response:\n\nWe don't exist on this world."
      }
    }
  ],
  "created": 1763411738,
  "model": "local",
  "system_fingerprint": "b7084-1a139644a",
  "object": "chat.completion",
  "usage": { "completion_tokens": 25, "prompt_tokens": 20, "total_tokens": 45 },
  "id": "chatcmpl-VJfSxuUqhW4ExLd0ea9ZnM18d1NEIcNU",
  "timings": { /* timing fields */ }
}
```

This is the raw JSON the app prints. The assistant's text is available at `choices[0].message.content`.

## Example `curl` (hitting a running model server)

Replace `<PORT>` with the model server port (e.g., the port printed by the app).

```bash
curl -X POST http://127.0.0.1:<PORT>/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model":"local","messages":[{"role":"user","content":"Selam"}]}'
```

Sample response from the server for the above request (trimmed):

```json
{"choices":[{"finish_reason":"stop","index":0,"message":{"role":"assistant","content":"I understand you're looking for a short answer. Here's a concise response:\n\nWe don't exist on this world."}}],"created":1763411738,"model":"local", ... }
```

## Suggestions / Next Steps

- Pretty-print or parse the returned JSON and extract `choices[0].message.content` to display only assistant text instead of the full JSON.
- Add `CancellationToken` support to both `CheckHealthAsync` and `CreateChatCompletionAsync`.
- Make the `system` role text configurable (currently it uses `"Short answer"`).

If you want, I can update `LlamaService.CreateChatCompletionAsync` to return a parsed object or just the assistant text and update `Program.cs` to display that more cleanly — tell me which option you prefer.
