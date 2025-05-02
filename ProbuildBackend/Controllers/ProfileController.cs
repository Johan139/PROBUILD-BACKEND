using Elastic.Apm.Api;
using Hangfire.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Services;
using System.IO.Compression;
using System.Net;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProfileController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ProgressHub> _hubContext;
        private readonly UserManager<UserModel> _userManager;
        private readonly AzureBlobService _azureBlobservice;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ProfileController(ApplicationDbContext context, UserManager<UserModel> userManager, IHubContext<ProgressHub> hubContext, IHttpContextAccessor httpContextAccessor = null, AzureBlobService azureBlobservice = null)
        {
            _context = context;
            _userManager = userManager;
            _httpContextAccessor = httpContextAccessor;
            _azureBlobservice = azureBlobservice;
            _hubContext = hubContext;
        }

        [HttpGet("GetTest")]
        public async Task<ActionResult<IEnumerable<UserModel>>> GetTest()
        {
            return Ok();
        }

        [HttpGet("GetDocuments/{UserId}")]
        public async Task<IActionResult> GetUserDocuments(string UserId)
        {
            var documents = await _context.ProfileDocuments
                .Where(doc => doc.UserId == UserId)
                .ToListAsync();

            if (documents == null || !documents.Any())
            {
                return NotFound();
            }

            var documentDetails = new List<object>();
            foreach (var doc in documents)
            {
                try
                {
                    var properties = await _azureBlobservice.GetBlobContentAsync(doc.BlobUrl);
                    documentDetails.Add(new
                    {
                        doc.Id,
                        doc.UserId,
                        doc.FileName,
                        Size = properties.Content.Length
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching properties for blob {doc.BlobUrl}: {ex.Message}");
                    documentDetails.Add(new
                    {
                        doc.Id,
                        doc.UserId,
                        doc.FileName,
                        Size = 0L
                    });
                }
            }

            return Ok(documentDetails);
        }

        [HttpGet("GetProfile/{id}")]
        public async Task<ActionResult<IEnumerable<UserModel>>> GetUserById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Id parameter cannot be null or empty.");

            var users = await _context.Users.Where(a => a.Id == id).ToListAsync();
            if (users == null || !users.Any())
                return NotFound("No users found with the specified id.");

            return Ok(users);
        }

        [HttpPost("Update")]
        public async Task<IActionResult> Update(RegisterDto model)
        {
            if (ModelState.IsValid)
            {
                var user = await _context.Users.Where(a => a.Id == model.Id).FirstOrDefaultAsync();
                user.Id = model.Id;
                user.UserName = model.Email;
                user.Email = model.Email;
                user.FirstName = model.FirstName;
                user.LastName = model.LastName;
                user.PhoneNumber = model.PhoneNumber;
                user.CompanyName = model.CompanyName;
                user.CompanyRegNo = model.CompanyRegNo;
                user.VatNo = model.VatNo;
                user.UserType = model.UserType;
                user.ConstructionType = model.ConstructionType;
                user.NrEmployees = model.NrEmployees;
                user.YearsOfOperation = model.YearsOfOperation;
                user.CertificationStatus = model.CertificationStatus;
                user.CertificationDocumentPath = model.CertificationDocumentPath;
                user.Availability = model.Availability;
                user.Trade = model.Trade;
                user.ProductsOffered = model.ProductsOffered;
                user.SupplierType = model.SupplierType;
                user.ProjectPreferences = model.ProjectPreferences;
                user.DeliveryArea = model.DeliveryArea;
                user.DeliveryTime = model.DeliveryTime;
                user.Country = model.Country;
                user.State = model.State;
                user.City = model.City;
                user.SubscriptionPackage = model.SubscriptionPackage;

                try
                {
                    var result = _context.SaveChangesAsync();


                    var address = new UserAddressModel
                    {
                        StreetNumber = model.StreetNumber,
                        StreetName = model.StreetName,
                        City = model.City,
                        State = model.State,
                        PostalCode = model.PostalCode,
                        Country = model.Country,
                        Latitude = model.Latitude,
                        Longitude = model.Longitude,
                        FormattedAddress = model.FormattedAddress,
                        GooglePlaceId = model.GooglePlaceId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        UserId = user.Id // This is now valid
                    };

                    _context.UserAddress.Add(address);
                    await _context.SaveChangesAsync(); // Save the address second

                    List<string> documentUrls = new List<string>();
                    if (!string.IsNullOrEmpty(model.SessionId))
                    {
                        var documents = await _context.ProfileDocuments
                            .Where(doc => doc.sessionId == model.SessionId && string.IsNullOrEmpty(doc.UserId))
                            .ToListAsync();

                        foreach (var doc in documents)
                        {
                            doc.UserId =model.Id;
                            documentUrls.Add(doc.BlobUrl);
                        }
                        await _context.SaveChangesAsync();
                    }

                    Console.WriteLine($"Profile ({user.Id}) updated successfully.");
                    return Ok(new { message = "Profile updated successfully." });
                }
                catch (Exception ex) {
                    return BadRequest(ex);
                }
            }
            return BadRequest(ModelState);
        }

        [HttpPost("UploadImage")]
        [RequestSizeLimit(200 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage([FromForm] UploadDocumentDTO jobRequest)
        {
            try
            {
                if (jobRequest == null)
                {
                    return BadRequest(new { error = "Invalid job request" });
                }

                if (jobRequest.Blueprint == null || !jobRequest.Blueprint.Any())
                {
                    return BadRequest(new { error = "No blueprint files provided" });
                }

                var allowedExtensions = new[] { ".pdf", ".png", ".jpg", ".jpeg" };
                var uploadedFileUrls = new List<string>();

                foreach (var file in jobRequest.Blueprint)
                {
                    if (file.Length == 0)
                    {
                        return BadRequest(new { error = $"Empty file detected: {file.FileName}" });
                    }

                    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (!allowedExtensions.Contains(extension))
                    {
                        return BadRequest(new { error = $"Invalid file type: {file.FileName}" });
                    }
                }

                string connectionId = jobRequest.connectionId ?? _httpContextAccessor.HttpContext?.Connection.Id
                    ?? throw new InvalidOperationException("No valid connectionId provided.");

                Console.WriteLine($"Received connectionId from client: {connectionId}");
                uploadedFileUrls = await _azureBlobservice.UploadFiles(jobRequest.Blueprint, _hubContext, connectionId);

                foreach (var (file, url) in jobRequest.Blueprint.Zip(uploadedFileUrls, (f, u) => (f, u)))
                {
                    string blobFileName = Path.GetFileName(new Uri(url).LocalPath);

                    Console.WriteLine($"Original file.FileName: {file.FileName}");
                    Console.WriteLine($"Blob URL from Azure: {url}");
                    Console.WriteLine($"Extracted Blob FileName: {blobFileName}");

                    var Document = new ProfileDocuments
                    {
                        UserId = "",
                        FileName = blobFileName,
                        BlobUrl = url,
                        sessionId = jobRequest.sessionId,
                        UploadedAt = DateTime.Now
                    };
                    _context.ProfileDocuments.Add(Document);
                }
                await _context.SaveChangesAsync();

                var response = new UploadDocumentModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Status = "Uploaded",
                    FileUrls = uploadedFileUrls,
                    FileNames = jobRequest.Blueprint.Select(f => f.FileName).ToList(),
                    Message = $"Successfully uploaded {jobRequest.Blueprint.Count} file(s)",
                    BillOfMaterials = null
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to upload files", details = ex.Message });
            }
        }

        [HttpGet("download/{documentId}")]
        public async Task<IActionResult> DownloadBlob(int documentId)
        {
            try
            {
                var document = await _context.ProfileDocuments
                    .FirstOrDefaultAsync(doc => doc.Id == documentId);

                if (document == null)
                {
                    return NotFound("Document not found.");
                }

                var (contentStream, contentType, originalFileName) = await _azureBlobservice.GetBlobContentAsync(document.BlobUrl);

                if (contentType == "application/gzip")
                {
                    using var decompressedStream = new MemoryStream();
                    using (var gzipStream = new GZipStream(contentStream, CompressionMode.Decompress))
                    {
                        await gzipStream.CopyToAsync(decompressedStream);
                    }
                    decompressedStream.Position = 0;
                    string decompressedContentType = GetContentTypeFromFileName(originalFileName);
                    return File(decompressedStream, decompressedContentType, originalFileName);
                }

                return File(contentStream, contentType, originalFileName);
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while fetching the blob: {ex.Message}");
            }
        }
        private string GetContentTypeFromFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                _ => "application/octet-stream"
            };
        }
    }
}
