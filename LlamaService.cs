using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Text;

namespace Demo.Conversation.AI
{
    /// <summary>
    /// Lightweight HTTP client wrapper for interacting with a local Llama-compatible
    /// model server. Provides health check and chat completion helper methods.
    /// </summary>
    public class LlamaService
    {
        // Reused HttpClient instance for the lifetime of this service.
        private readonly HttpClient _client;

        // Base URL of the model server (e.g. "http://127.0.0.1:19390").
        private readonly string _baseUrl;

        /// <summary>
        /// Create a new <see cref="LlamaService"/>.
        /// </summary>
        /// <param name="baseUrl">The base URL of the model server (no trailing slash required).</param>
        public LlamaService(string baseUrl)
        {
            // Instantiate HttpClient once to avoid socket exhaustion from repeated creation.
            _client = new HttpClient();
            _baseUrl = baseUrl;
        }

        /// <summary>
        /// Calls the server health endpoint (<c>GET {baseUrl}/v1/health</c>) and returns
        /// the raw response body as a string. Throws on non-success HTTP status codes.
        /// </summary>
        /// <returns>Raw response body from the health endpoint (usually JSON).</returns>
        public async Task<string> CheckHealthAsync()
        {
            // Send a simple GET request to the health endpoint.
            var response = await _client.GetAsync($"{_baseUrl}/v1/health");

            // Throw an exception if the server returned an error status code.
            response.EnsureSuccessStatusCode();

            // Return the response body as-is (caller can parse JSON if desired).
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Sends a chat completion request to the server (<c>POST {baseUrl}/v1/chat/completions</c>).
        /// The method constructs a minimal OpenAI-like chat payload containing a system message
        /// and a single user message, serializes it to JSON, and returns the raw server response.
        /// </summary>
        /// <param name="model">Model identifier (e.g. "local").</param>
        /// <param name="userMessage">The user's prompt text to send to the model.</param>
        /// <returns>Raw response body from the chat completion endpoint (JSON string).</returns>
        public async Task<string> CreateChatCompletionAsync(string model, string userMessage)
        {
            // Build an anonymous payload that matches a common chat completion API shape.
            // Includes a small 'system' instruction to bias the assistant and the user's message.
            var payload = new
            {
                model = model,
                messages = new[] {
                    new { role = "system", content = "Short answer" },
                    new { role = "user", content = userMessage }
                }
            };

            // Serialize the payload to JSON. The default JsonSerializer options are used
            // (this keeps the code minimal). For more control, pass JsonSerializerOptions.
            string json = JsonSerializer.Serialize(payload);

            // Wrap the JSON in a StringContent with the correct media type header.
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Post the JSON payload to the chat completions endpoint.
            var response = await _client.PostAsync($"{_baseUrl}/v1/chat/completions", content);

            // Read the response text before throwing so we can return or log it if needed.
            string responseText = await response.Content.ReadAsStringAsync();

            // Throw for HTTP error codes. Caller can catch exceptions and inspect responseText
            // if desired for debugging (current API throws after reading the body).
            response.EnsureSuccessStatusCode();

            // Return the raw JSON response. The caller (e.g., the Program) is responsible
            // for parsing or pretty-printing the result.
            return responseText;
        }
    }
}
