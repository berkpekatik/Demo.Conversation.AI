using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using Demo.Conversation.AI;

// See https://aka.ms/new-console-template for more information

public class Program
{
    public static async Task Main(string[] args)
    {
        string currentDir = AppDomain.CurrentDomain.BaseDirectory;
        string folderPath = Path.Combine(currentDir, "llama.cpp");

        if (!Directory.Exists(folderPath))
        {
            Console.WriteLine("Error: llama.cpp folder not found.");
            Environment.Exit(1);
        }

        string exePath = Path.Combine(folderPath, "llama-server.exe");

        if (!File.Exists(exePath))
        {
            Console.WriteLine("Error: llama-server.exe not found in llama.cpp folder.");
            Environment.Exit(1);
        }

        Random random = new Random();
        int port = random.Next(1024, 65536);

        Process? serverProcess = null;

        void CleanupServer()
        {
            try
            {
                if (serverProcess != null && !serverProcess.HasExited)
                {
                    // Kill the server process (and its tree) and wait a short time for exit
                    serverProcess.Kill(entireProcessTree: true);
                    serverProcess.WaitForExit(5000);
                }
            }
            catch
            {
                // best effort cleanup, ignore exceptions
            }
        }

        // Ensure server is stopped on normal exit, Ctrl+C or process exit
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true; // allow graceful shutdown
            CleanupServer();
            Environment.Exit(0);
        };
        AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanupServer();

        serverProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--model models\\ai.gguf --host 127.0.0.1 --port {port} --no-webui",
                WorkingDirectory = folderPath,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        serverProcess.Start();

        string baseUrl = $"http://127.0.0.1:{port}";
        LlamaService service = new LlamaService(baseUrl);

        Console.WriteLine($"Server running at {baseUrl}");

        await DelayWithCountdownAsync(10000);

        try
        {
            string health = await service.CheckHealthAsync();
            Console.WriteLine($"Health check: {health}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Health check error: {ex.Message}");
        }

        // allow typing "exit" to stop the server and quit
        while (true)
        {
            Console.Write("Enter prompt (type 'exit' to quit): ");
            string prompt = Console.ReadLine() ?? string.Empty;
            if (string.Equals(prompt, "exit", StringComparison.OrdinalIgnoreCase))
            {
                CleanupServer();
                break;
            }

            try
            {
                // send the user's prompt to the model and show returned data
                string result = await service.CreateChatCompletionAsync("local", prompt);
                Console.WriteLine("Model response:\n" + result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending prompt: {ex.Message}");
            }
        }
    }

    private static async Task DelayWithCountdownAsync(int milliseconds, CancellationToken cancellationToken = default)
    {
        int totalSeconds = (int)Math.Ceiling(milliseconds / 1000.0);
        for (int remaining = totalSeconds; remaining > 0; remaining--)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            Console.Write($"Waiting {remaining} second{(remaining == 1 ? "" : "s")}...\r");
            try
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
        // Clear the line after countdown completes
        Console.Write(new string(' ', Console.WindowWidth > 0 ? Math.Min(Console.WindowWidth - 1, 80) : 80) + "\r");
    }
}
