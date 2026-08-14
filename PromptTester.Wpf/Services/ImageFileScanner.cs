using System.IO;
using PromptTester.Wpf.Models;

namespace PromptTester.Wpf.Services;

public static class ImageFileScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif",
        ".bmp",
        ".tif",
        ".tiff"
    };

    public static IReadOnlyList<DocumentImageSet> GetDocuments(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Choose a document root folder before running.");
        }

        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"The folder does not exist: {folderPath}");
        }

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.System
        };
        var allFiles = new List<string>();
        foreach (var path in Directory.EnumerateFiles(folderPath, "*", enumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            allFiles.Add(path);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var pdfDocuments = allFiles
            .Where(path => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, NaturalStringComparer.Instance)
            .Select(path => new DocumentImageSet
            {
                DocumentName = Path.GetFileNameWithoutExtension(path),
                DocumentPath = path,
                SourceType = "PDF",
                PdfPath = path
            });

        var imageDocuments = allFiles
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => new DocumentImageSet
            {
                DocumentName = Path.GetFileName(group.Key) ?? group.Key ?? "",
                DocumentPath = group.Key ?? "",
                SourceType = "Images",
                ImagePaths = group.OrderBy(path => path, NaturalStringComparer.Instance).ToList()
            })
            .OrderBy(document => document.DocumentPath, NaturalStringComparer.Instance);

        var documents = pdfDocuments.Concat(imageDocuments)
            .OrderBy(document => document.DocumentPath, NaturalStringComparer.Instance)
            .ToList();
        cancellationToken.ThrowIfCancellationRequested();
        return documents;
    }
}
