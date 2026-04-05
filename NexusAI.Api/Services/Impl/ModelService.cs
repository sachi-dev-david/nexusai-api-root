using System.Net.Http.Json;
using NexusAI.Api.Models;

namespace NexusAI.Api.Services.Impl;

public class ModelService : IModelService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ModelService> _logger;

    public ModelService(IHttpClientFactory httpClientFactory, ILogger<ModelService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<AllModelsStatus> GetAllModelsStatusAsync()
    {
        var ollamaTask = GetOllamaStatusAsync();
        var visionTask = GetVisionStatusAsync();
        var mathTask = GetMathStatusAsync();

        await Task.WhenAll(ollamaTask, visionTask, mathTask);

        return new AllModelsStatus
        {
            Ollama = ollamaTask.Result,
            Vision = visionTask.Result,
            Math = mathTask.Result
        };
    }

    public async Task<ModelStatus> GetOllamaStatusAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Ollama");
            var response = await client.GetAsync("/api/tags");
            
            if (response.IsSuccessStatusCode)
            {
                return new ModelStatus
                {
                    Name = "llama3.2:latest",
                    Status = "online",
                    Message = "模型運行中"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama health check failed");
        }

        return new ModelStatus
        {
            Name = "llama3.2:latest",
            Status = "offline",
            Message = "模型未連線"
        };
    }

    public async Task<ModelStatus> GetVisionStatusAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Vision");
            var response = await client.GetAsync("/health");
            
            if (response.IsSuccessStatusCode)
            {
                return new ModelStatus
                {
                    Name = "Vision",
                    Status = "online",
                    Message = "特徵辨識模型運行中"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vision health check failed");
        }

        return new ModelStatus
        {
            Name = "Vision",
            Status = "offline",
            Message = "特徵辨識模型未連線"
        };
    }

    public async Task<ModelStatus> GetMathStatusAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Math");
            var response = await client.GetAsync("/health");
            
            if (response.IsSuccessStatusCode)
            {
                return new ModelStatus
                {
                    Name = "Math",
                    Status = "online",
                    Message = "數學模型運行中"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Math health check failed");
        }

        return new ModelStatus
        {
            Name = "Math",
            Status = "offline",
            Message = "數學模型未連線"
        };
    }
}
