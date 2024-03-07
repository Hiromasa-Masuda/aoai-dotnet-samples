using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ChatStreamingWebApp001.Controllers;

[ApiController]
[Route("[controller]")]
public class ChatStreamingController : ControllerBase
{           
    private readonly OpenAIClient _openAIClient;
    private readonly string _modelName;
    private readonly string _systemMessage;

    private readonly ILogger<ChatStreamingController> _logger;

    public ChatStreamingController(IConfiguration configuration, ILogger<ChatStreamingController> logger)
    {
        var endpoint = configuration["azOpenAiEndpoint"] ?? throw new ArgumentNullException("azOpenAiEndpoint is not set.");
        var key = configuration["azOpenAiApiKey"] ?? throw new ArgumentNullException("azOpenAiApiKey is not set.");            

        _openAIClient = new OpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(key));

        _modelName = configuration["azOpenAiModelName"] ?? throw new ArgumentNullException("azOpenAiModelName is not set.");

        _systemMessage =
        """
        ���Ȃ��͘a�̖̂���ł��B�^����ꂽ��𒚔J�ɉ�����Ă��������B
        ����ɁA���ǂ���ɂ��邽�߂̓Y����s���A�Y��̃|�C���g�𒚔J�ɉ�����Ă��������B
        """;

        _logger = logger;
    }
    
    [HttpPost]
    public async Task Post([FromBody] string message, CancellationToken cancellationToken)
    {            
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Content-Type", "text/event-stream");

        var writer = new StreamWriter(Response.Body);

        ChatCompletionsOptions chatCompletionsOptions = new()
        {
            DeploymentName = _modelName,
            Messages =
            {
                new ChatRequestSystemMessage(_systemMessage),
                new ChatRequestUserMessage(message),
            }
        };

        var messageId = Guid.NewGuid().ToString();            

        _logger.LogInformation("server - response started.");

        await foreach (StreamingChatCompletionsUpdate chatUpdate in 
            _openAIClient.GetChatCompletionsStreaming(chatCompletionsOptions)                
            .WithCancellation(cancellationToken)) // when clinet connection aborted.
        {

            var json = new { id = messageId, role = chatUpdate.Role?.ToString(), content = chatUpdate.ContentUpdate, createdDateTime = DateTimeOffset.Now };
            var jsonString = JsonSerializer.Serialize(json);
            
            await writer.WriteAsync($"data: {jsonString}\n\n");
            await writer.FlushAsync();

            _logger.LogTrace("{json}", jsonString);
            
            //await Task.Delay(33);
        }

        string doneEvent = "data: [DONE]\n\n";

        await writer.WriteLineAsync(doneEvent);
        await writer.FlushAsync();

        _logger.LogTrace(doneEvent);

        _logger.LogInformation("server - response ended. isCancellationRequested={IsCancellationRequested}.", 
            cancellationToken.IsCancellationRequested);
    }
}
