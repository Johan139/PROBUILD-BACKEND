using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Helpers;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Services;
using System.Globalization;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TradePackagesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IAiAnalysisService _aiAnalysisService;
        private readonly UserManager<UserModel> _userManager;
        private readonly AzureBlobService _azureBlobservice;
        public TradePackagesController(
            ApplicationDbContext context,
                        AzureBlobService azureBlobservice,
                      UserManager<UserModel> userManager,
            IAiAnalysisService aiAnalysisService
        )
        {
            _context = context;
            _aiAnalysisService = aiAnalysisService;
            _userManager = userManager;
            _azureBlobservice = azureBlobservice;
        }

        [HttpGet("{jobId}")]
        public async Task<ActionResult<IEnumerable<TradePackage>>> GetTradePackages(int jobId)
        {
            return await _context.TradePackages.Where(tp => tp.JobId == jobId).ToListAsync();
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTradePackage(int id, TradePackage tradePackage)
        {
            if (id != tradePackage.Id)
            {
                return BadRequest();
            }

            _context.Entry(tradePackage).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.TradePackages.Any(e => e.Id == id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }
        [HttpPut("archivepackage/{id}")]
        public async Task<IActionResult> ArchivePackage(int id)
        {


            try
            {
                var archivePackage = _context.TradePackages.Where(m => m.Id == id).FirstOrDefault();
                archivePackage.ArchivedAt = DateTime.Now;

                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.TradePackages.Any(e => e.Id == id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }
        [HttpPost("{id}/post")]
        public async Task<IActionResult> PostToMarketplace(int id)
        {
            var tradePackage = await _context.TradePackages.FindAsync(id);
            if (tradePackage == null)
            {
                return NotFound();
            }

            tradePackage.PostedToMarketplace = true;
            tradePackage.Status = "Posted";
            await _context.SaveChangesAsync();

            return Ok(new { message = "Package posted to marketplace" });
        }

        [HttpPost("simple")]
        [RequestSizeLimit(50 * 1024 * 1024)] // 50MB for blueprint
        public async Task<IActionResult> PostSimpleJob([FromForm] SimpleJobDto jobRequest)
        {
            if (jobRequest == null)
            {
                return BadRequest("Job request cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(jobRequest.FullName) ||
                string.IsNullOrWhiteSpace(jobRequest.Email) ||
                string.IsNullOrWhiteSpace(jobRequest.JobDescription))
            {
                return BadRequest("Full name, email, and job description are required.");
            }

            // Validate trades if tradesman is selected
            if (jobRequest.ProfessionalType == "tradesman" &&
                (jobRequest.SelectedTrades == null || jobRequest.SelectedTrades.Count == 0))
            {
                return BadRequest("At least one trade must be selected when choosing tradesman.");
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // 1. Find or create temp user
                    string userId;
                    var normalizedEmail = jobRequest.Email.ToUpperInvariant();
                    var existingUser = await _userManager.FindByEmailAsync(jobRequest.Email);

                    if (existingUser != null)
                    {
                        userId = existingUser.Id;
                    }
                    else
                    {
                        var nameParts = jobRequest.FullName.Trim().Split(' ', 2);
                        var firstName = nameParts[0];
                        var lastName = nameParts.Length > 1 ? nameParts[1] : "";

                        var placeholderUser = new UserModel
                        {
                            Id = Guid.NewGuid().ToString(),
                            UserName = jobRequest.Email,
                            Email = jobRequest.Email,
                            NormalizedUserName = normalizedEmail,
                            NormalizedEmail = normalizedEmail,
                            FirstName = firstName,
                            LastName = lastName,
                            PhoneNumber = jobRequest.PhoneNumber ?? "",
                            CompanyName = "",
                            CompanyRegNo = "",
                            VatNo = "",
                            UserType = "",
                            ConstructionType = null,
                            NrEmployees = "",
                            YearsOfOperation = "",
                            CertificationStatus = "",
                            CertificationDocumentPath = "",
                            Availability = "",
                            Trade = "",
                            ProductsOffered = "",
                            SupplierType = "",
                            JobPreferences = null,
                            DeliveryArea = null,
                            DeliveryTime = "",
                            SubscriptionPackage = "",
                            DateCreated = DateTime.UtcNow,
                            CountryNumberCode = "",
                            isPlaceholder = true
                        };

                        var result = await _userManager.CreateAsync(
                            placeholderUser,
                            PasswordGenerator.GenerateRandomPassword(12)
                        );

                        if (!result.Succeeded)
                        {
                            return BadRequest($"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                        }

                        userId = placeholderUser.Id;
                    }

                    // 2. Create the job
                    var projectTypeName = jobRequest.ProjectType == "new-build" ? "New Build" : "Renovation";

                    var job = new JobModel
                    {
                        TradeBudgets = null,
                        ProjectName = $"{projectTypeName} - {jobRequest.CityArea}",
                        JobType = projectTypeName,
                        Qty = 1,
                        DesiredStartDate = DateTime.UtcNow.AddDays(30),
                        WallStructure = "Pending AI Analysis",
                        WallStructureSubtask = null,
                        WallInsulation = "Pending AI Analysis",
                        WallInsulationSubtask = null,
                        RoofStructure = "Pending AI Analysis",
                        RoofStructureSubtask = null,
                        RoofTypeSubtask = null,
                        RoofInsulation = "Pending AI Analysis",
                        RoofInsulationSubtask = null,
                        Foundation = "Pending AI Analysis",
                        FoundationSubtask = null,
                        Finishes = "Pending AI Analysis",
                        FinishesSubtask = null,
                        ElectricalSupplyNeeds = "Pending AI Analysis",
                        ElectricalSupplyNeedsSubtask = null,
                        Stories = 0,
                        BuildingSize = 0,
                        OperatingArea = jobRequest.CityArea,
                        Address = jobRequest.CityArea,
                        UserId = userId,
                        Status = "BIDDING",
                        BiddingType = "PUBLIC",
                        Blueprint = "",
                        CreatedAt = DateTime.UtcNow,
                    };

                    _context.Jobs.Add(job);
                    await _context.SaveChangesAsync();

                    // 3. Handle Address if provided
                    if (!string.IsNullOrEmpty(jobRequest.Address))
                    {
                        decimal lat = Math.Round(
                            Convert.ToDecimal(jobRequest.Latitude, CultureInfo.InvariantCulture),
                            6
                        );
                        decimal lon = Math.Round(
                            Convert.ToDecimal(jobRequest.Longitude, CultureInfo.InvariantCulture),
                            6
                        );
                        var address = new AddressModel
                        {
                            FormattedAddress = jobRequest.Address,
                            StreetNumber = jobRequest.StreetNumber,
                            StreetName = jobRequest.StreetName,
                            City = jobRequest.City,
                            State = jobRequest.State,
                            PostalCode = jobRequest.PostalCode,
                            Country = jobRequest.Country,
                            Latitude = lat,
                            Longitude = lon,
                            GooglePlaceId = jobRequest.GooglePlaceId,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                            JobId = job.Id,
                        };

                        job.Address = jobRequest.Address;
                        job.JobAddress = address;

                        _context.JobAddresses.Add(address);
                        await _context.SaveChangesAsync();
                    }

                    // 4. Upload blueprint file if provided
                    var uploadedFileUrls = new List<string>();
                    if (jobRequest.HasBlueprint && jobRequest.BlueprintFile != null)
                    {
                        // Validate file type
                        var allowedExtensions = new[] { ".pdf", ".png", ".jpg", ".jpeg" };
                        var extension = Path.GetExtension(jobRequest.BlueprintFile.FileName).ToLowerInvariant();

                        if (!allowedExtensions.Contains(extension))
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { error = $"Invalid file type: {jobRequest.BlueprintFile.FileName}. Allowed types: PDF, PNG, JPG, JPEG" });
                        }

                        if (jobRequest.BlueprintFile.Length == 0)
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { error = "Empty file detected" });
                        }

                        // Upload to Azure Blob Storage
                        uploadedFileUrls = await _azureBlobservice.UploadFiles(
                            new List<IFormFile> { jobRequest.BlueprintFile },
                            null,
                            null
                        );

                        if (uploadedFileUrls.Any())
                        {
                            var url = uploadedFileUrls.First();
                            string blobFileName = Path.GetFileName(new Uri(url).LocalPath);

                            // Create JobDocument record
                            var jobDocument = new JobDocumentModel
                            {
                                JobId = job.Id,
                                FileName = blobFileName,
                                BlobUrl = url,
                                SessionId = jobRequest.SessionId,
                                UploadedAt = DateTime.UtcNow,
                            };

                            _context.JobDocuments.Add(jobDocument);

                            // Update job's Blueprint field with the URL
                            job.Blueprint = url;

                            await _context.SaveChangesAsync();
                        }
                    }

                    // 5. Create TradePackage(s)
                    var tradePackages = new List<TradePackage>();

                    if (jobRequest.ProfessionalType == "general-contractor")
                    {
                        var tradePackage = new TradePackage
                        {
                            JobId = job.Id,
                            TradeName = "New Build",
                            Category = "Trade",
                            ScopeOfWork = jobRequest.JobDescription,
                            Budget = 0,
                            Status = "Posted",
                            EstimatedManHours = 0,
                            HourlyRate = 0,
                            PostedToMarketplace = true,
                            CreatedAt = DateTime.UtcNow
                        };
                        tradePackages.Add(tradePackage);
                    }
                    else
                    {
                        foreach (var trade in jobRequest.SelectedTrades!)
                        {
                            var tradePackage = new TradePackage
                            {
                                JobId = job.Id,
                                TradeName = trade,
                                Category = "Trade",
                                ScopeOfWork = jobRequest.JobDescription,
                                Budget = 0,
                                Status = "Posted",
                                EstimatedManHours = 0,
                                HourlyRate = 0,
                                PostedToMarketplace = true,
                                CreatedAt = DateTime.UtcNow
                            };
                            tradePackages.Add(tradePackage);
                        }
                    }

                    _context.TradePackages.AddRange(tradePackages);
                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();

                    return Ok(new
                    {
                        success = true,
                        jobId = job.Id,
                        tradePackageIds = tradePackages.Select(tp => tp.Id).ToList(),
                        userId = userId,
                        isNewUser = existingUser == null,
                        blueprintUrl = uploadedFileUrls.FirstOrDefault()
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, $"An error occurred: {ex.Message}");
                }
            });
        }

        [HttpPost("{jobId}/refresh")]
        public async Task<IActionResult> RefreshTradePackages(int jobId)
        {
            try
            {
                await _aiAnalysisService.RefreshTradePackagesAsync(jobId);
                return Ok(new { message = "Trade packages refreshed successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new { error = "Failed to refresh trade packages", details = ex.Message }
                );
            }
        }

        [HttpGet("user/{userId}/postings")]
        public async Task<ActionResult<IEnumerable<object>>> GetUserMarketplacePostings(
            string userId
        )
        {
            try
            {
                var postings = await _context
                    .TradePackages.Where(tp => tp.Job.UserId == userId && tp.PostedToMarketplace && tp.ArchivedAt == null)
                    .Include(tp => tp.Job)
                        .ThenInclude(j => j.JobAddress)
                    .Select(tp => new
                    {
                        tp.Id,
                        tp.TradeName,
                        tp.Category,
                        tp.ScopeOfWork,
                        tp.Budget,
                        tp.Status,
                        tp.EstimatedManHours,
                        tp.HourlyRate,
                        tp.EstimatedDuration,
                        tp.StartDate,
                        tp.BidDeadline,
                        tp.LaborType,
                        tp.CsiCode,
                        tp.PostedToMarketplace,
                        tp.CreatedAt,
                        JobId = tp.JobId,
                        ProjectName = tp.Job.ProjectName,
                        Address = tp.Job.Address
                            ?? (
                                tp.Job.JobAddress != null ? tp.Job.JobAddress.FormattedAddress : ""
                            ),
                        City = tp.Job.JobAddress != null ? tp.Job.JobAddress.City : "",
                        State = tp.Job.JobAddress != null ? tp.Job.JobAddress.State : "",
                    })
                    .OrderByDescending(tp => tp.CreatedAt)
                    .ToListAsync();

                return Ok(postings);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        error = "Failed to fetch user marketplace postings",
                        details = ex.Message,
                    }
                );
            }
        }
    }
}
