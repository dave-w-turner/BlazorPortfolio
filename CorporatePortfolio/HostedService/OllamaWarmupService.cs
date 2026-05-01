namespace CorporatePortfolio.HostedService
{
    using System.Diagnostics;
    using System.Text;
    using System.Text.Json;

    public class OllamaWarmupService(HttpClient client, string modelName) : BackgroundService
    {
        private readonly string ModelName = modelName;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Give the server a moment to breathe before hitting the API
            await Task.Delay(3000, stoppingToken);

            // Send an empty prompt to trigger the model load
            // Setting keep_alive to -1 pins it in memory indefinitely
            var request = new
            {
                model = ModelName,
                prompt = "",
                keep_alive = -1
            };

            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

            // Use the 'generate' endpoint for a quick ping
            var response = await client.PostAsync("api/generate", content, stoppingToken);

            if (!response.IsSuccessStatusCode)
                throw new Exception("❌ Failed to warm up Ollama. Status: {StatusCode}", new Exception($"Status Code: {response.StatusCode}"));

            Debug.WriteLine("✅ Ollama is WARM and ready for requests!");
        }
    }
}
