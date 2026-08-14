using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using PromptTester.Wpf.Models;

namespace PromptTester.Wpf.Services;

public sealed partial class OpenAiExtractionService
{
    private const long MaxPdfBytes = 50L * 1024L * 1024L;
    private const int MaxImageInputs = 1_500;
    private const int MaxOutputTokens = 16_384;
    private const int MaxDetailedAuditFields = 100;
    private const int MaxAutoImagesPerRequest = 15;

    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://api.openai.com/v1/"),
        Timeout = TimeSpan.FromMinutes(10)
    };

    public async Task<ExtractionResult> ExtractAsync(
        DocumentImageSet document,
        string extractionPrompt,
        IReadOnlyList<string> fields,
        ModelDefinition model,
        string inputDetail,
        string apiKey,
        CancellationToken cancellationToken)
    {
        inputDetail = NormalizeInputDetail(inputDetail);
        if (document.PdfPath is null
            && inputDetail == "auto"
            && document.ImagePaths.Count > MaxAutoImagesPerRequest)
        {
            return await ExtractImageChunksAsync(
                document,
                extractionPrompt,
                fields,
                model,
                inputDetail,
                apiKey,
                cancellationToken);
        }

        return await ExtractSingleAsync(
            document,
            extractionPrompt,
            fields,
            model,
            inputDetail,
            apiKey,
            cancellationToken);
    }

    private async Task<ExtractionResult> ExtractImageChunksAsync(
        DocumentImageSet document,
        string extractionPrompt,
        IReadOnlyList<string> fields,
        ModelDefinition model,
        string inputDetail,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var chunkResults = new List<ExtractionResult>();
        var chunks = document.ImagePaths.Chunk(MaxAutoImagesPerRequest).ToArray();

        for (var index = 0; index < chunks.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = new DocumentImageSet
            {
                DocumentName = $"{document.DocumentName} (pages {index * MaxAutoImagesPerRequest + 1}-{index * MaxAutoImagesPerRequest + chunks[index].Length})",
                DocumentPath = document.DocumentPath,
                SourceType = document.SourceType,
                ImagePaths = chunks[index]
            };

            var chunkResult = await ExtractSingleAsync(
                chunk,
                extractionPrompt,
                fields,
                model,
                inputDetail,
                apiKey,
                cancellationToken);
            chunkResults.Add(chunkResult);

            if (chunkResult.StopBatch)
            {
                break;
            }
        }

        stopwatch.Stop();
        return MergeChunkResults(document, fields, model, chunks.Length, chunkResults, stopwatch.Elapsed);
    }

    private static async Task<ExtractionResult> ExtractSingleAsync(
        DocumentImageSet document,
        string extractionPrompt,
        IReadOnlyList<string> fields,
        ModelDefinition model,
        string inputDetail,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new ExtractionResult
        {
            DocumentName = document.DocumentName,
            DocumentPath = document.DocumentPath,
            SourceType = document.SourceType,
            ImageCount = document.InputCount,
            Model = model.Id
        };

        try
        {
            if (string.IsNullOrWhiteSpace(extractionPrompt))
            {
                throw new InvalidOperationException("The extraction prompt cannot be empty.");
            }

            var requestBody = await Task.Run(
                () => BuildRequestBody(document, extractionPrompt, fields, model, inputDetail, cancellationToken),
                cancellationToken);
            var requestJson = await Task.Run(() => JsonSerializer.Serialize(requestBody), cancellationToken);
            var apiResponse = await SendRequestAsync(requestJson, apiKey, cancellationToken);

            if (!apiResponse.IsSuccessStatusCode && model.SupportsReasoningSummary)
            {
                var initialError = ExtractApiError(apiResponse.Json, apiResponse.StatusCode.ToString());
                if (IsReasoningSummaryAvailabilityError(initialError))
                {
                    result.Warnings.Add("The official reasoning summary was unavailable for this account/model. The request was retried without the summary while preserving low reasoning effort and the structured decision rationale.");
                    requestBody["reasoning"] = new
                    {
                        effort = "low"
                    };
                    requestJson = await Task.Run(() => JsonSerializer.Serialize(requestBody), cancellationToken);
                    apiResponse = await SendRequestAsync(requestJson, apiKey, cancellationToken);
                }
            }

            if (!apiResponse.IsSuccessStatusCode)
            {
                if (apiResponse.Attempts > 1)
                {
                    result.Warnings.Add($"The API request still failed after {apiResponse.Attempts} attempts; transient retries were exhausted.");
                }

                var error = ExtractApiError(apiResponse.Json, apiResponse.StatusCode.ToString());
                result.Status = "error";
                result.CompletionStatus = "request_error";
                result.Error = error.Message;
                result.StopBatch = ShouldStopBatch(apiResponse.StatusCode, error);
                EnsureEmptyValues(result, fields);
                return result;
            }

            if (apiResponse.Attempts > 1)
            {
                result.Warnings.Add($"The API request succeeded after {apiResponse.Attempts} attempts due to a transient rate-limit or service response.");
            }

            ApplySuccessfulResponse(apiResponse.Json, result, fields, model);
            result.Status = "success";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            EnsureEmptyValues(result, fields);
            result.Status = "error";
            result.CompletionStatus = "timeout";
            result.StopBatch = true;
            result.Error = $"Timed out after {HttpClient.Timeout.TotalMinutes:0} minutes while processing this document.";
        }
        catch (HttpRequestException ex)
        {
            EnsureEmptyValues(result, fields);
            result.Status = "error";
            result.CompletionStatus = "network_error";
            result.StopBatch = true;
            result.Error = $"Could not reach the OpenAI API: {ex.Message}";
        }
        catch (Exception ex)
        {
            EnsureEmptyValues(result, fields);
            result.Status = "error";
            result.CompletionStatus = string.IsNullOrWhiteSpace(result.CompletionStatus) ? "processing_error" : result.CompletionStatus;
            result.Error = ex.Message;
        }
        finally
        {
            stopwatch.Stop();
            result.ProcessingSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 2);
        }

        return result;
    }

}
