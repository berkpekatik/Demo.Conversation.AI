# scenerio-unity

This document explains the recommended approach for integrating `llama-server.exe` with a Unity game: start the server when the game starts, verify it's healthy before allowing gameplay, handle dynamic port checks, and ensure the server is stopped when the game closes.

---

## Goals (from your text)
- When the game starts, launch `llama-server.exe` with the required parameters.
- Show the player a game menu while the server is starting/being checked.
- Verify the server is OK by calling the health endpoint before proceeding.
- Be cautious about port selection: if the chosen port is already used, try a different port and re-check.
- On game exit, stop the running server process (if it was started by the game).

---

## High-level flow
1. On game start (or when player selects "Start Game"), attempt to launch `llama-server.exe` with arguments such as:
   ```text
   --model models\\ai.gguf --host 127.0.0.1 --port {port} --no-webui
   ```
2. While the server starts, show the main menu and an indicator (e.g., "Starting model... 10s") so the user knows the game is waiting.
3. Poll the server health endpoint (`GET http://127.0.0.1:{port}/v1/health`) with a small retry loop until it returns success or a timeout is reached.
4. If the health check fails because the port is in use or the server didn't start, try another port (or surface a clear error and let the user retry).
5. When the game quits or the user exits, gracefully stop the server process that the game started.

---

## Key safety and reliability considerations
- Always start `llama-server.exe` with `UseShellExecute = false` and `CreateNoWindow = true` when starting from Unity on Windows.
- Set `WorkingDirectory` to the folder that contains `llama-server.exe` so relative model paths work.
- Reuse a controlled `Process` instance so you can stop it later. Track whether the game started the process vs. attaching to an already-running server.
- If `Process.Kill()` does not remove child processes, fall back to calling `taskkill /PID {pid} /T /F` on Windows to ensure the server tree is terminated.
- Perform retries for the health endpoint (exponential backoff or fixed-delay attempts) rather than assuming a single attempt will be enough.
- Be careful about antivirus or OS policy blocking execution; test in the target environment.

---

## Example Unity C# script
This is a practical starting point you can drop into a Unity project. It uses `System.Diagnostics.Process` to start the server, `UnityWebRequest` for the health check, and listens for `Application.quitting` to stop the server.

```csharp
// LlamaServerManager.cs
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class LlamaServerManager : MonoBehaviour
{
    public string LlamaFolder = "llama.cpp"; // relative to Application.dataPath or set absolute path
    public string ExeName = "llama-server.exe";
    public string ModelArg = "models\\ai.gguf";

    public int PortStart = 19390; // starting port to try
    public int MaxPortAttempts = 10;
    public int HealthRetryDelayMs = 1000;
    public int HealthMaxAttempts = 10;

    private Process _serverProcess;
    private int _port;
    private bool _startedByGame = false;

    async void Start()
    {
        // Optional: show menu UI here while starting
        Application.quitting += OnQuitting;

        // Attempt to start and validate the server
        bool ok = await StartAndValidateServerAsync();
        if (ok)
        {
            Debug.Log($"Llama server ready at http://127.0.0.1:{_port}");
            // Now you can enable game UI or network features that depend on the model.
        }
        else
        {
            Debug.LogError("Failed to start or validate the llama server.");
            // Surface UI to let the user retry or continue without a model.
        }
    }

    private async Task<bool> StartAndValidateServerAsync()
    {
        for (int attempt = 0; attempt < MaxPortAttempts; attempt++)
        {
            int tryPort = PortStart + attempt; // simple incremental approach
            if (StartServerProcess(tryPort))
            {
                // wait and poll health endpoint
                bool healthy = await PollHealthAsync(tryPort);
                if (healthy)
                {
                    _port = tryPort;
                    _startedByGame = true;
                    return true;
                }
                else
                {
                    // server didn't become healthy on this port; kill and try next
                    StopServerProcess();
                }
            }
            else
            {
                // couldn't start process on this port (maybe busy). try next.
            }
        }

        return false;
    }

    private bool StartServerProcess(int port)
    {
        try
        {
            string exePath = System.IO.Path.Combine(Application.dataPath, LlamaFolder, ExeName);
            if (!System.IO.File.Exists(exePath))
            {
                Debug.LogError($"llama server exe not found: {exePath}");
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--model {ModelArg} --host 127.0.0.1 --port {port} --no-webui",
                WorkingDirectory = System.IO.Path.Combine(Application.dataPath, LlamaFolder),
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            _serverProcess = Process.Start(psi);
            return _serverProcess != null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to start llama server on port {port}: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> PollHealthAsync(int port)
    {
        string url = $"http://127.0.0.1:{port}/v1/health";
        for (int i = 0; i < HealthMaxAttempts; i++)
        {
            try
            {
                using (UnityWebRequest req = UnityWebRequest.Get(url))
                {
                    var op = req.SendWebRequest();
                    while (!op.isDone)
                        await Task.Yield();

#if UNITY_2020_1_OR_NEWER
                    if (req.result == UnityWebRequest.Result.Success)
#else
                    if (!req.isNetworkError && !req.isHttpError)
#endif
                    {
                        // success; server answered the health endpoint
                        Debug.Log($"Health check OK: {req.downloadHandler.text}");
                        return true;
                    }
                    else
                    {
                        Debug.Log($"Health check failed attempt {i+1}: {req.error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"Health check exception: {ex.Message}");
            }

            await Task.Delay(HealthRetryDelayMs);
        }

        return false;
    }

    private void StopServerProcess()
    {
        if (_serverProcess == null) return;

        try
        {
            if (!_serverProcess.HasExited)
            {
                _serverProcess.Kill();
                _serverProcess.WaitForExit(2000);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to stop llama server process: {ex.Message}");
            // On Windows, consider fallback: run taskkill /PID {pid} /T /F
        }
        finally
        {
            _serverProcess?.Dispose();
            _serverProcess = null;
            _startedByGame = false;
        }
    }

    private void OnQuitting()
    {
        // When the game quits, ensure we stop the server we started.
        if (_startedByGame)
        {
            StopServerProcess();
        }
    }

    private void OnDestroy()
    {
        // Also guard for object destruction while game is running.
        OnQuitting();
    }
}
```

### Notes about the example
- `Application.dataPath` is used to find `llama.cpp`. Adjust the path for builds (e.g., use `Application.streamingAssetsPath` or a configurable absolute path).
- Unity's `Process` API is system-level; in the Editor paths and permissions differ from a built player.
- For robust termination of child processes on Windows, if `Process.Kill()` doesn't remove children, use `taskkill /PID {pid} /T /F` as a fallback (run it via `ProcessStartInfo` and `cmd.exe /c taskkill ...`).

---

## Alternate port strategy
- Use a fixed config port if you want reproducible URLs. If that port is in use, either:
  - Ask the user to free the port, or
  - Automatically pick a random free port: try to bind a TcpListener on port 0 to get a free port number, then start the server on that port.

Example to get a free TCP port in C# before starting server:

```csharp
int GetFreePort()
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}
```

Caveat: race conditions can happen between selecting a free port and starting the external process; the port might be claimed by another process in between. A retry loop is still recommended.

---

## UI / menu UX suggestions
- Show a clear status while starting: `Starting model... (attempt 2/10)` and a spinner.
- Allow the user to continue into the game without the model (graceful degradation) or show an error with a "Retry" button.
- Log the full server stdout/stderr to a file for debugging (capture `ProcessStartInfo.RedirectStandardOutput` and `RedirectStandardError` if available on your target platform).

---

## Shutdown behavior
- Prefer a graceful shutdown (if the server supports a shutdown endpoint) before `Kill()`.
- If no graceful API exists, `Kill()` is acceptable but make best effort to stop child processes too.

---

## Quick checklist for implementation and testing
- [ ] Confirm `llama-server.exe` path and working directory.
- [ ] Choose port strategy: fixed vs dynamic.
- [ ] Implement start + health check polling with retries.
- [ ] Surface clear UI state in the main menu during startup.
- [ ] Ensure server termination on `Application.quitting` / `OnDestroy`.
- [ ] Test in Editor and in Windows build; check antivirus/permission issues.

---

If you want, I can:
- Provide a Unity package with a simple sample scene and UI that demonstrates the flow above, or
- Modify the project `Program.cs` to expose a small helper console app that Unity could call to manage the server (useful if you prefer the process managed outside the player).

Which follow-up would you like? (sample scene, Unity package, or helper console tool?)