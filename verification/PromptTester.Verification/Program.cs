using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using PromptTester.Wpf.Models;
using PromptTester.Wpf.Services;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertDecimal(decimal actual, decimal expected, string message)
{
    if (Math.Abs(actual - expected) > 0.000000001m)
    {
        throw new InvalidOperationException($"{message}. Expected {expected}, got {actual}.");
    }
}

Assert(ModelCatalog.Models.Count == 11, "Model dropdown should expose eleven priced document models");
Assert(ModelCatalog.Find("gpt-5.6")?.Id == "gpt-5.6-sol", "GPT-5.6 alias should resolve to Sol");
foreach (var listedModel in ModelCatalog.Models)
{
    var estimate = ModelCatalog.CalculateCost(listedModel, 1_000, 100, 0, 100);
    Assert(estimate.TotalCostUsd > 0, $"Listed model has no usable cost rule: {listedModel.Id}");
}

var terra = ModelCatalog.Find("gpt-5.6-terra")!;
Assert(terra.PriceCaption.Contains("cache write", StringComparison.OrdinalIgnoreCase), "GPT-5.6 price caption should disclose cache-write pricing");
Assert(terra.PriceCaption.Contains("Above 272K", StringComparison.Ordinal), "Long-context pricing should be visible in the model card");
var shortCost = ModelCatalog.CalculateCost(terra, 200_000, 20_000, 20_000, 20_000);
AssertDecimal(shortCost.InputCostUsd, 0.374m, "GPT-5.6 Terra cache-read/write pricing is incorrect");
AssertDecimal(shortCost.OutputCostUsd, 0.24m, "GPT-5.6 Terra output pricing is incorrect");
AssertDecimal(shortCost.TotalCostUsd, 0.614m, "GPT-5.6 Terra total pricing is incorrect");
Assert(!shortCost.UsedLongContextPricing, "A 200,000-token request should use short-context pricing");

var luna = ModelCatalog.Find("gpt-5.6-luna")!;
var lunaCost = ModelCatalog.CalculateCost(luna, 200_000, 20_000, 20_000, 20_000);
AssertDecimal(lunaCost.InputCostUsd, 0.0374m, "GPT-5.6 Luna cache-read/write pricing is incorrect");
AssertDecimal(lunaCost.OutputCostUsd, 0.024m, "GPT-5.6 Luna output pricing is incorrect");
AssertDecimal(lunaCost.TotalCostUsd, 0.0614m, "GPT-5.6 Luna total pricing is incorrect");

var thresholdCost = ModelCatalog.CalculateCost(terra, 272_000, 0, 0, 10_000);
var longCost = ModelCatalog.CalculateCost(terra, 272_001, 0, 0, 10_000);
Assert(!thresholdCost.UsedLongContextPricing, "272,000 tokens should use short-context pricing");
Assert(longCost.UsedLongContextPricing, "272,001 tokens should use long-context pricing");

var parsedFields = FieldParser.Parse("invoice_number, total\nInvoice_Number\r\ncurrency");
Assert(parsedFields.SequenceEqual(new[] { "invoice_number", "total", "currency" }), "Field parser should split mixed line endings and remove duplicates");

var largeFieldSet = string.Join('\n', Enumerable.Range(1, 224).Select(index => $"field_{index}"));
var parsedLargeFieldSet = FieldParser.Parse(largeFieldSet);
Assert(parsedLargeFieldSet.Count == 224, "Field parser should accept extraction profiles with more than 100 fields");

var schemaBuilder = typeof(OpenAiExtractionService).GetMethod(
    "BuildJsonSchema",
    BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("Extraction schema builder was not found");
var compactSchema = JsonSerializer.Serialize(schemaBuilder.Invoke(null, [parsedLargeFieldSet]));
Assert(compactSchema.Contains("\"values\"", StringComparison.Ordinal), "Large profiles should retain their values schema");
Assert(!compactSchema.Contains("\"field_decisions\"", StringComparison.Ordinal), "Large profiles should omit field-level audit output");
Assert(!compactSchema.Contains("\"description\"", StringComparison.Ordinal), "Large profiles should not repeat every field name in value descriptions");
var detailedSchema = JsonSerializer.Serialize(schemaBuilder.Invoke(null, [parsedFields]));
Assert(detailedSchema.Contains("\"field_decisions\"", StringComparison.Ordinal), "Smaller profiles should retain field-level audit output");

var valueMerger = typeof(OpenAiExtractionService).GetMethod(
    "MergeFieldValues",
    BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("Chunk value merger was not found");
var mergedScalar = (string?)valueMerger.Invoke(null, [new string?[] { null, "00123", "00123" }]);
var mergedArray = (string?)valueMerger.Invoke(null, [new string?[] { "[\"A\",\"B\"]", "B", "C" }]);
Assert(mergedScalar == "00123", "Chunk merging should retain one unique scalar value");
Assert(mergedArray == "[\"A\",\"B\",\"C\"]", "Chunk merging should flatten and deduplicate array values");

var decisionMerger = typeof(OpenAiExtractionService).GetMethod(
    "MergeFieldDecisions",
    BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("Chunk decision merger was not found");
var mergedDecision = (FieldDecision?)decisionMerger.Invoke(
    null,
    [
        new FieldDecision?[]
        {
            new()
            {
                Status = "missing",
                Evidence = null,
                Explanation = "Not present on the first page chunk.",
                Confidence = "high"
            },
            new()
            {
                Status = "extracted",
                Evidence = "Invoice No. 00123",
                Explanation = "Found on the second page chunk.",
                Confidence = "medium"
            }
        },
        true
    ]);
Assert(mergedDecision?.Status == "extracted", "A later extracted chunk should replace an earlier missing decision");
Assert(mergedDecision?.Evidence == "Invoice No. 00123", "Merged decisions should retain evidence for the extracted value");
Assert(mergedDecision?.Explanation == "Found on the second page chunk.", "Missing-chunk explanations should not contradict an extracted value");
Assert(mergedDecision?.Confidence == "medium", "Merged decisions should retain the most conservative relevant confidence");

var verificationRoot = Path.Combine(Path.GetTempPath(), $"PromptTester-verify-{Guid.NewGuid():N}");
Directory.CreateDirectory(verificationRoot);
try
{
    var imageFolder = Path.Combine(verificationRoot, "Document A");
    Directory.CreateDirectory(imageFolder);
    File.WriteAllBytes(Path.Combine(imageFolder, "page_10.png"), []);
    File.WriteAllBytes(Path.Combine(imageFolder, "page_2.png"), []);
    File.WriteAllBytes(Path.Combine(imageFolder, "page_1.png"), []);
    var scanned = ImageFileScanner.GetDocuments(verificationRoot);
    Assert(scanned.Count == 1, "Image folder should be grouped as one document");
    Assert(scanned[0].ImagePaths.Select(Path.GetFileName).SequenceEqual(new[] { "page_1.png", "page_2.png", "page_10.png" }), "Pages should use natural numeric order");
    var reportPath = Path.Combine(verificationRoot, "report.xlsx");
    ExcelReportWriter.ValidateTarget(reportPath);
    var extraction = new ExtractionResult
    {
        DocumentName = "Document A",
        DocumentPath = imageFolder,
        SourceType = "Images",
        ImageCount = 3,
        Status = "success",
        Model = terra.Id,
        ResponseId = "resp_test",
        CompletionStatus = "completed",
        InputTokens = 1000,
        OutputTokens = 100,
        TotalTokens = 1100,
        InputCostUsd = 0.0025m,
        OutputCostUsd = 0.0015m,
        TotalCostUsd = 0.004m,
        HasCostEstimate = true,
        PricingLabel = "Standard API",
        DecisionSummary = new string('x', 40_000),
        ProcessingSeconds = 1.25
    };
    extraction.Values["invoice_number"] = "00123";
    extraction.Values["___"] = "symbol-only field remains identifiable";
    extraction.FieldDecisions["invoice_number"] = new FieldDecision
    {
        Status = "extracted",
        Evidence = "Invoice No. 00123",
        Explanation = "Copied the visible identifier.",
        Confidence = "high"
    };

    var logFolder = DecisionLogWriter.CreateRunFolder(reportPath);
    extraction.DecisionLogPath = DecisionLogWriter.Write(logFolder, 1, extraction, ["invoice_number"], "Extract the invoice number.");
    var logText = File.ReadAllText(extraction.DecisionLogPath);
    Assert(logText.Contains("does not contain hidden chain-of-thought", StringComparison.OrdinalIgnoreCase), "Decision log should state the reasoning boundary");
    Assert(logText.Contains("00123", StringComparison.Ordinal), "Decision log should include extracted values and evidence");

    ExcelReportWriter.Write([extraction], ["invoice_number", "___"], reportPath);
    Assert(File.Exists(reportPath), "Excel report was not created");
    using var archive = ZipFile.OpenRead(reportPath);
    using var sheetReader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
    var sheetXml = sheetReader.ReadToEnd();
    Assert(sheetXml.Contains("t=\"n\"", StringComparison.Ordinal), "Excel numeric values should be emitted as numeric cells");
    Assert(sheetXml.Contains("00123", StringComparison.Ordinal), "Excel should preserve leading-zero field values as text");
    Assert(sheetXml.Contains(">___<", StringComparison.Ordinal), "Symbol-only field names should remain identifiable in Excel");
    Assert(sheetXml.Contains("truncated for Excel", StringComparison.Ordinal), "Oversized cell text should carry an explicit truncation notice");
    var worksheet = System.Xml.Linq.XDocument.Parse(sheetXml);
    var spreadsheetNamespace = (System.Xml.Linq.XNamespace)"http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    var longestCellText = worksheet.Descendants(spreadsheetNamespace + "t").Max(element => element.Value.Length);
    Assert(longestCellText <= 32_767, "No Excel cell may exceed the 32,767-character limit");
}
finally
{
    if (Directory.Exists(verificationRoot))
    {
        Directory.Delete(verificationRoot, recursive: true);
    }
}

Console.WriteLine("PromptTester verification checks passed.");
