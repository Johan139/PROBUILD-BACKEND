using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using ProbuildBackend.Interface;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Services;
using System.IO.Compression;

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
        public IConfiguration _configuration;

        public ProfileController(ApplicationDbContext context, UserManager<UserModel> userManager, IHubContext<ProgressHub> hubContext, IHttpContextAccessor httpContextAccessor = null, AzureBlobService azureBlobservice = null, IConfiguration configuration = null)
        {
            _context = context;
            _userManager = userManager;
            _httpContextAccessor = httpContextAccessor;
            _azureBlobservice = azureBlobservice;
            _hubContext = hubContext;
            _configuration = configuration;
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

        [HttpGet("getusersubscription/{userId}")]
        public async Task<ActionResult<IEnumerable<PaymentRecord>>> GetUserSubscription(string userId)
        {
            try
            {

      
            if (string.IsNullOrWhiteSpace(userId))
                return BadRequest("Id parameter cannot be null or empty.");

            var PaymentRecord = await _context.PaymentRecords
                .Where(p => p.UserId == userId).ToListAsync();

            if (PaymentRecord == null || !PaymentRecord.Any())
                return NotFound("No payment record found with the specified user id.");


            return Ok(PaymentRecord);
            }
            catch (Exception ex)
            {

                throw;
            }
        }


        [HttpGet("GetProfile/{id}")]
        public async Task<ActionResult<IEnumerable<UserModel>>> GetUserById(string id)
        {
            try
            {


            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Id parameter cannot be null or empty.");

            var user = await _context.Users
                .Include(u => u.Portfolio)
                .ThenInclude(p => p.Jobs)
                .FirstOrDefaultAsync(a => a.Id == id);


            if (user == null)
                return NotFound("No user found with the specified id.");

            user.UserAddresses = _context.UserAddress.Where(p => p.UserId == id && (p.Deleted == false || p.Deleted == null)).ToList();

            return Ok(user);

            }
            catch (Exception ex)
            {

                throw;
            }

        }

        [HttpPost("Update")]
        public async Task<IActionResult> Update(RegisterDto model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(a => a.Id == model.Id);
                if (user == null)
                    return NotFound("User not found.");
                var oldSubscription = user.SubscriptionPackage;
                // Update user fields
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
                user.JobPreferences = model.JobPreferences;
                user.DeliveryArea = model.DeliveryArea;
                user.DeliveryTime = model.DeliveryTime;
                user.CountryNumberCode = model.CountryNumberCode;
                //user.Country = model.Country;
                //user.State = model.State;
                //user.City = model.City;
                user.SubscriptionPackage = model.SubscriptionPackage;

                // Add address (can be done before save)
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
                    UserId = user.Id
                };
                _context.UserAddress.Add(address);

                // Update documents
                if (!string.IsNullOrEmpty(model.SessionId))
                {
                    var documents = await _context.ProfileDocuments
                        .Where(doc => doc.sessionId == model.SessionId && string.IsNullOrEmpty(doc.UserId))
                        .ToListAsync();

                    foreach (var doc in documents)
                    {
                        doc.UserId = model.Id;
                    }
                }

                // Now commit all changes once
                await _context.SaveChangesAsync();

                Console.WriteLine($"Profile ({user.Id}) updated successfully.");
                return Ok(new { message = "Profile updated successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
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

        [HttpGet("managesubscriptions/{userId}")]
        public async Task<IActionResult> ManageSubscription(string userId)
        {
            try
            {
                var normUserId = (userId ?? "").Trim().ToLower();

                // Get the viewer's email once (for matching AssignedUser by email)
                var viewerEmail = await _context.Users.AsNoTracking()
                    .Where(u => (u.Id ?? "").ToLower() == normUserId)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync();

                var normEmail = (viewerEmail ?? "").Trim().ToLower();

                var result = await _context.PaymentRecords.AsNoTracking()
                    .Where(pr =>
                        // payer
                        ((pr.UserId ?? "").ToLower().Trim() == normUserId)
                        // assignee by id
                        || ((pr.AssignedUser ?? "").ToLower().Trim() == normUserId)
                        // assignee by email
                        || (normEmail != "" && ((pr.AssignedUser ?? "").ToLower().Trim() == normEmail))
                    )
                    .Select(pr => new UserPaymentRecordDTO
                    {
                        Package = pr.Package,
                        ValidUntil = pr.ValidUntil,
                        Amount = pr.Amount,
                        AssignedUser = pr.AssignedUser,   // raw AssignedUser (id/email)
                        Status = pr.Status,
                        SubscriptionID = pr.SubscriptionID,

                        // Resolve a displayable name/email for the assignee (id OR email)
                        AssignedUserName = _context.Users.AsNoTracking()
                            .Where(u =>
                                ((u.Id ?? "").ToLower() == (pr.AssignedUser ?? "").ToLower().Trim()) ||
                                ((u.Email ?? "").ToLower() == (pr.AssignedUser ?? "").ToLower().Trim())
                            )
                            .Select(u => u.Email)          // or $"{u.FirstName} {u.LastName}" if you prefer
                            .FirstOrDefault()
                    })
                    .ToListAsync();

                return Ok(result);
            }
            catch (Exception ex)
            {
                // log ex
                return StatusCode(500, "Failed to load subscriptions.");
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
        [HttpPost("AddUserAddress")]
        public async Task<IActionResult> AddUserAddress(UserAddressDTO address)
        {
            if (address == null || string.IsNullOrEmpty(address.UserId))
                return BadRequest("Invalid address payload or missing userId.");

            try
            {

                var userAddressModel = new UserAddressModel
                {
                    StreetNumber = address.StreetNumber,
                    StreetName = address.StreetName,
                    State = address.State,
                    Country = address.Country,
                    City = address.City,
                    PostalCode = address.PostalCode,
                    CountryCode = address.CountryCode,
                    FormattedAddress = address.FormattedAddress,
                    GooglePlaceId = address.GooglePlaceId,
                    Latitude = address.Latitude,
                    Longitude = address.Longitude,
                    UserId = address.UserId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    AddressType = address.AddressType,

                };
                _context.UserAddress.Add(userAddressModel);
                await _context.SaveChangesAsync();

                return Ok(address);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to add user address", details = ex.Message });
            }
        }

        [HttpPut("UpdateUserAddress/{id:int}")]
        public async Task<IActionResult> UpdateUserAddress(int id, [FromBody] UserAddressModel updated)
        {
            if (updated == null)
                return BadRequest("Invalid payload.");

            var existing = await _context.UserAddress.FirstOrDefaultAsync(a => a.Id == id);
            if (existing == null)
                return NotFound("Address not found.");

            try
            {
                existing.FormattedAddress = updated.FormattedAddress;
                existing.GooglePlaceId = updated.GooglePlaceId;
                existing.Latitude = updated.Latitude;
                existing.Longitude = updated.Longitude;
                existing.StreetNumber = updated.StreetNumber;
                existing.StreetName = updated.StreetName;
                existing.City = updated.City;
                existing.State = updated.State;
                existing.PostalCode = updated.PostalCode;
                existing.Country = updated.Country;
                existing.CountryCode = updated.CountryCode;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.AddressType = updated.AddressType;

                await _context.SaveChangesAsync();
                return Ok(existing);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to update address", details = ex.Message });
            }
        }

        [HttpDelete("DeleteUserAddress/{id:int}")]
        public async Task<IActionResult> DeleteUserAddress(int id)
        {
            var address = await _context.UserAddress.FirstOrDefaultAsync(a => a.Id == id);
            if (address == null)
                return NotFound("Address not found.");

            address.Deleted = true;

            try
            {
                _context.UserAddress.Add(address);
                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to delete address", details = ex.Message });
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
        [HttpGet("AddressTypes")]
        public IActionResult GetAddressTypes()
        {
            var types = _context.AddressType
                .OrderBy(t => t.DisplayOrder)
                .ToList();

            return Ok(types);
        }
    }
}
