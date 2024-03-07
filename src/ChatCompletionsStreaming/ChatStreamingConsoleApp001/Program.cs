using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace ChatStreamingConsoleApp001;

internal class Program
{
    private static HttpClient _httpClient = new HttpClient();
    private static ILogger? _logger = null;

    static async Task Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder =>
            builder
                .AddConfiguration(configuration.GetSection("Logging"))
                .AddSimpleConsole(options => 
                {
                    options.TimestampFormat = "[HH:mm:ss.fff] ";                    
                })
                .AddDebug());

        _logger = loggerFactory.CreateLogger(nameof(Program));        


        AzureEventSourceLogForwarder azureEventSourceLogForwarder = new(loggerFactory);
        azureEventSourceLogForwarder.Start();

        var azOpenAiEndpoint = configuration["azOpenAiEndpoint"] ?? throw new ArgumentNullException("azOpenAiEndpoint is not set.");
        var azOpenAiApiKey = configuration["azOpenAiApiKey"] ?? throw new ArgumentNullException("azOpenAiApiKey is not set.");
        var azOpenAiModelName = configuration["azOpenAiModelName"] ?? throw new ArgumentNullException("azOpenAiModelName is not set.");
        var customApiEndpoint = configuration["customChatStreamingApiEndpoint"] ?? throw new ArgumentNullException("customChatStreamingApiEndpoint is not set.");

        var systemMessage =
            """
            あなたは和歌の名手です。与えられた句を丁寧に解説してください。
            さらに、より良い句にするための添削を行い、添削のポイントを丁寧に解説してください。
            """;

        var userMessage =
            """
            この世をば　わが世とぞ思ふ　望月の　欠けたることも　なしと思へば
            """;        

        CancellationTokenSource cancellationTokenSource = new();
        var cancellationToken = cancellationTokenSource.Token;

        while (true)
        {
            Console.WriteLine("Enter the command number and press the enter key.");
            Console.WriteLine("1: GetChatCompletionsStreaming Demo, 2: GetChatCompletionsStreaming via Custom API Demo");

            Console.Write("> ");
            string? inputLine = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(inputLine))
            {
                continue;
            }

            if (inputLine.ToLowerInvariant() == "exit")
            {
                break;
            }

            if (inputLine.ToLowerInvariant() == "1")
            {
                await GetChatCompletionsStreamingAsync(azOpenAiEndpoint, azOpenAiApiKey, azOpenAiModelName, systemMessage, userMessage, 33, cancellationToken);
            }
            else if (inputLine.ToLowerInvariant() == "2")
            {
                await GetChatCompletionsStreamingViaCustomApiAsync(customApiEndpoint, userMessage, 33, cancellationToken);
            }
            else
            {
                Console.WriteLine("invalid command number.");
            }

            await Task.Delay(1000);

            Console.WriteLine();
        }
    }

    private static async Task GetChatCompletionsStreamingAsync(
        string aoaiEndpoint, string aoaiApiKey, string aoaiModelName,
        string systemMessage, string userMessage, 
        int outputMillisecondsDelay,
        CancellationToken cancellationToken)
    {
        OpenAIClientOptions clientOptions = new()
        {
            Diagnostics =
            {
                IsLoggingContentEnabled = true,
                LoggedContentSizeLimit = 1024 * 1024,
            }
        };

        OpenAIClient openAIClient = new(new Uri(aoaiEndpoint), new AzureKeyCredential(aoaiApiKey), clientOptions);

        ChatCompletionsOptions chatCompletionsOptions = new()
        {
            DeploymentName = aoaiModelName,
            Messages =
            {
                new ChatRequestSystemMessage(systemMessage),
                new ChatRequestUserMessage(userMessage),
            }
        };

        await foreach (var chatUpdate in 
            openAIClient.GetChatCompletionsStreaming(chatCompletionsOptions, cancellationToken)
            .ConfigureAwait(false))
        {
            if (chatUpdate.Role.HasValue)
            {
                Console.WriteLine($"\n{chatUpdate.Role.Value}: ");
            }
            if (!string.IsNullOrEmpty(chatUpdate.ContentUpdate))
            {
                Console.Write(chatUpdate.ContentUpdate);
            }

            await Task.Delay(outputMillisecondsDelay).ConfigureAwait(false);
        }

        Console.WriteLine();
    }    

    private static async Task GetChatCompletionsStreamingViaCustomApiAsync(
        string customApiEndpoint, string userMessage, 
        int outputMillisecondsDelay, 
        CancellationToken cancellationToken)
    {
        HttpRequestMessage httpRequestMessage = new()
        {
            Content = JsonContent.Create(userMessage),
            Method = HttpMethod.Post,
            RequestUri = new Uri(customApiEndpoint),            
        };

        _logger?.LogInformation("Request {method} {httpVersion} {uri} {headers} {contentHeaders}", httpRequestMessage.Method, httpRequestMessage.Version, httpRequestMessage.RequestUri, httpRequestMessage.Headers.ToString(), httpRequestMessage.Content.Headers.ToString());        

        var response = await _httpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Response {statusCode} {headers} {contentHeaders}", response.StatusCode, response.Headers.ToString(), response.Content.Headers.ToString());        
        _logger?.LogInformation("client - response started.");
        

        await foreach (var jsonNode in
            ReadSseStreamingAsync(response, cancellationToken)
            .ConfigureAwait(false))
        {
            if (jsonNode == null)
            {
                continue;
            }

            var role = jsonNode?["role"]?.GetValue<string>();
            var chunkedContent = jsonNode?["content"]?.GetValue<string>();

            if (role != null)
            {
                Console.WriteLine($"\n{role}: ");
            }

            if (chunkedContent != null)
            {
                Console.Write(chunkedContent);
            }
            
            await Task.Delay(outputMillisecondsDelay).ConfigureAwait(false);
        }

        Console.WriteLine();
        _logger?.LogInformation("client - response ended.");
    }

    private static async IAsyncEnumerable<JsonNode?> ReadSseStreamingAsync(
        HttpResponseMessage httpResponseMessage,        
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Stream responseStream = await httpResponseMessage.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        using (var streamReader = new StreamReader(responseStream))
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await streamReader.ReadLineAsync().ConfigureAwait(false);

                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }
                else if (line == "data: [DONE]")
                {
                    break;
                }
                else if (line.StartsWith("data: "))
                {
                    var body = line.Substring(6, line.Length - 6);
                    yield return JsonSerializer.Deserialize<JsonNode>(body);                    
                }
            }
        };
    }    
}
