using Microsoft.Extensions.Logging;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class BlueprintProcessingService : IBlueprintProcessingService
{
    private readonly IAiAnalysisService _aiAnalysisService;
    private readonly IPdfConversionService _pdfConversionService;
    private readonly AzureBlobService _blobService;
    private readonly ILogger<BlueprintProcessingService> _logger;
    private readonly ApplicationDbContext _context;

    public BlueprintProcessingService(
        IAiAnalysisService aiAnalysisService,
        IPdfConversionService pdfConversionService,
        AzureBlobService blobService,
        ILogger<BlueprintProcessingService> logger,
        ApplicationDbContext context)
    {
        _aiAnalysisService = aiAnalysisService;
        _pdfConversionService = pdfConversionService;
        _blobService = blobService;
        _logger = logger;
        _context = context;
    }

    public async Task<BlueprintAnalysisDto> ProcessBlueprintAsync(string userId, string pdfUrl, int jobId)
    {
        _logger.LogInformation("Starting blueprint processing for Job ID: {JobId}", jobId);

        try
        {
            var analysisJson = await _aiAnalysisService.PerformBlueprintAnalysisAsync(userId, new List<string> { pdfUrl });
            
            var (pdfStream, _, fileName) = await _blobService.GetBlobContentAsync(pdfUrl);
            
            var tempFileNamePrefix = Path.GetRandomFileName();
            var localImagePaths = _pdfConversionService.ConvertPdfToImages(pdfStream, tempFileNamePrefix);

            var pageImageUrls = new List<string>();
            foreach (var localPath in localImagePaths)
            {
                await using var imageStream = new FileStream(localPath, FileMode.Open);
                var imageUrl = await _blobService.UploadFileAsync(Path.GetFileName(localPath), imageStream, "image/png", jobId);
                pageImageUrls.Add(imageUrl);
                File.Delete(localPath);
                _logger.LogInformation("Uploaded image {ImagePath} to {ImageUrl}", localPath, imageUrl);
            }

            var blueprintEntity = new BlueprintAnalysis
            {
                JobId = jobId,
                OriginalFileName = fileName,
                PdfUrl = pdfUrl,
                PageImageUrlsJson = JsonSerializer.Serialize(pageImageUrls),
                AnalysisJson = analysisJson,
                TotalPages = pageImageUrls.Count
            };

            _context.BlueprintAnalyses.Add(blueprintEntity);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully saved BlueprintAnalysis entity for Job ID: {JobId}", jobId);

            return new BlueprintAnalysisDto
            {
                Id = blueprintEntity.Id,
                JobId = jobId,
                Name = blueprintEntity.OriginalFileName,
                PdfUrl = blueprintEntity.PdfUrl,
                PageImageUrls = pageImageUrls,
                AnalysisJson = blueprintEntity.AnalysisJson,
                TotalPages = blueprintEntity.TotalPages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing blueprint for Job ID: {JobId}", jobId);
            throw;
        }
    }
}