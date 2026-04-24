using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BuildigBackend.Interface;
using BuildigBackend.Models;
using BuildigBackend.Services;

namespace BuildigBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmailTemplatesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly AzureBlobService _blobService;
        private readonly IEmailSender _emailSender;

        public EmailTemplatesController(ApplicationDbContext context, AzureBlobService blobService, IEmailSender emailSender)
        {
            _context = context;
            _blobService = blobService;
            _emailSender = emailSender;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var templates = await _context.EmailTemplates
                .AsNoTracking()
                .Select(t => new
                {
                    t.TemplateId,
                    t.TemplateName,
                    t.Subject,
                    t.Description,
                    t.LanguageCode,
                    t.VersionNumber,
                    t.IsActive,
                    t.CreatedDate,
                    t.ModifiedDate
                })
                .OrderBy(t => t.TemplateName)
                .ToListAsync();

            return Ok(templates);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var template = await _context.EmailTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TemplateId == id);

            if (template == null)
                return NotFound();

            return Ok(template);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] EmailTemplate created)
        {
            if (created == null)
                return BadRequest(new { error = "request is required" });

            var template = new EmailTemplate
            {
                TemplateName = created.TemplateName,
                Subject = created.Subject,
                Description = created.Description,
                FromName = created.FromName,
                FromEmail = created.FromEmail,
                IsHtml = created.IsHtml,
                HeaderHtml = created.HeaderHtml,
                FooterHtml = created.FooterHtml,
                LogoUrl = created.LogoUrl,
                InlineCss = created.InlineCss,
                LanguageCode = created.LanguageCode,
                IsActive = created.IsActive,
                Body = created.Body,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow,
                VersionNumber = 1,
            };

            _context.EmailTemplates.Add(template);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = template.TemplateId }, template);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] EmailTemplate updated)
        {
            var template = await _context.EmailTemplates
                .FirstOrDefaultAsync(t => t.TemplateId == id);

            if (template == null)
            {
                template = new EmailTemplate
                {
                    CreatedDate = DateTime.UtcNow,
                    VersionNumber = 1,
                };
                _context.EmailTemplates.Add(template);
            }

            template.TemplateName = updated.TemplateName;
            template.Subject = updated.Subject;
            template.Description = updated.Description;
            template.FromName = updated.FromName;
            template.FromEmail = updated.FromEmail;
            template.IsHtml = updated.IsHtml;
            template.HeaderHtml = updated.HeaderHtml;
            template.FooterHtml = updated.FooterHtml;
            template.LogoUrl = updated.LogoUrl;
            template.InlineCss = updated.InlineCss;
            template.LanguageCode = updated.LanguageCode;
            template.IsActive = updated.IsActive;
            template.Body = updated.Body;
            template.ModifiedDate = DateTime.UtcNow;

            if (template.VersionNumber <= 0)
                template.VersionNumber = 1;
            else
                template.VersionNumber = template.VersionNumber + 1;

            await _context.SaveChangesAsync();

            return Ok(new { templateId = template.TemplateId });
        }

        public class EmailTemplateSendTestRequestDto
        {
            public string ToEmail { get; set; } = string.Empty;
            public string? Subject { get; set; }
            public string? Body { get; set; }
            public string? FromEmail { get; set; }
            public string? FromName { get; set; }
            public string? TemplateName { get; set; }
        }

        [HttpPost("{id}/send-test")]
        public async Task<IActionResult> SendTest(int id, [FromBody] EmailTemplateSendTestRequestDto request)
        {
            if (request == null)
                return BadRequest(new { error = "request is required" });

            if (string.IsNullOrWhiteSpace(request.ToEmail))
                return BadRequest(new { error = "toEmail is required" });

            if (string.IsNullOrWhiteSpace(request.Body))
                return BadRequest(new { error = "body is required" });

            var dbTemplate = await _context.EmailTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TemplateId == id);

            var template = new EmailTemplate
            {
                TemplateId = id,
                TemplateName = request.TemplateName ?? dbTemplate?.TemplateName,
                Subject = request.Subject ?? dbTemplate?.Subject,
                Body = request.Body,
                FromEmail = request.FromEmail ?? dbTemplate?.FromEmail,
                FromName = request.FromName ?? dbTemplate?.FromName,
                IsHtml = true,
            };

            await _emailSender.SendEmailAsync(template, request.ToEmail.Trim());

            return Ok(new { ok = true });
        }

        public class EmailTemplateAssetListItemDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
        }

        public class EmailTemplateAssetListResponseDto
        {
            public List<EmailTemplateAssetListItemDto> Assets { get; set; } = new();
        }

        [HttpGet("assets")]
        public async Task<IActionResult> ListAssets([FromQuery] string kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
                return BadRequest(new { error = "kind is required" });

            kind = kind.Trim().ToLowerInvariant();
            if (kind != "header" && kind != "footer")
                return BadRequest(new { error = "kind must be 'header' or 'footer'" });

            var assets = await _context.EmailTemplateAssets
                .AsNoTracking()
                .Where(a =>
                    a.Kind == kind && (a.Url.StartsWith("http://") || a.Url.StartsWith("https://"))
                )
                .OrderByDescending(a => a.CreatedDate)
                .ToListAsync();

            var dto = assets
                .Select(a => new EmailTemplateAssetListItemDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    Url = _blobService.GenerateTemporaryPublicUrl(a.Url),
                })
                .ToList();

            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, proxy-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            return Ok(new EmailTemplateAssetListResponseDto { Assets = dto });
        }

        public class EmailTemplateAssetSasRequestDto
        {
            public string Kind { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public string ContentType { get; set; } = string.Empty;
        }

        public class EmailTemplateAssetSasResponseDto
        {
            public string UploadUrl { get; set; } = string.Empty;
            public string PublicUrl { get; set; } = string.Empty;
        }

        [HttpPost("assets/sas")]
        public async Task<IActionResult> CreateAssetSas([FromBody] EmailTemplateAssetSasRequestDto request)
        {
            return StatusCode(
                StatusCodes.Status410Gone,
                new
                {
                    error = "SAS upload is deprecated. Use POST /api/EmailTemplates/assets/upload instead.",
                }
            );
        }

        public class EmailTemplateAssetUploadResponseDto
        {
            public string Url { get; set; } = string.Empty;
        }

        [HttpPost("assets/upload")]
        [RequestSizeLimit(50 * 1024 * 1024)]
        public async Task<IActionResult> UploadAsset([FromQuery] string kind, [FromForm] IFormFile file)
        {
            if (string.IsNullOrWhiteSpace(kind))
                return BadRequest(new { error = "kind is required" });

            kind = kind.Trim().ToLowerInvariant();
            if (kind != "header" && kind != "footer")
                return BadRequest(new { error = "kind must be 'header' or 'footer'" });

            if (file == null || file.Length == 0)
                return BadRequest(new { error = "file is required" });

            var folder = $"email-templates/{kind}";
            var url = await _blobService.UploadImageAsync(file, folder);

            var asset = new EmailTemplateAsset
            {
                Kind = kind,
                Name = file.FileName,
                Url = url,
                CreatedDate = DateTime.UtcNow,
            };

            _context.EmailTemplateAssets.Add(asset);
            await _context.SaveChangesAsync();

            var displayUrl = _blobService.GenerateTemporaryPublicUrl(url);

            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, proxy-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            return Ok(new EmailTemplateAssetUploadResponseDto { Url = displayUrl });
        }

        [HttpDelete("assets/{id:int}")]
        public async Task<IActionResult> DeleteAsset(int id)
        {
            var asset = await _context.EmailTemplateAssets.FirstOrDefaultAsync(a => a.Id == id);
            if (asset == null)
                return NotFound();

            var blobUrl = asset.Url;

            _context.EmailTemplateAssets.Remove(asset);
            await _context.SaveChangesAsync();

            await _blobService.DeleteBlobIfExistsAsync(blobUrl);

            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, proxy-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            return NoContent();
        }
    }
}
