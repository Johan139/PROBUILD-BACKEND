using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Helpers;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Services;

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

        [HttpGet("job/{jobId}/bid-invites/counts")]
        public async Task<IActionResult> GetBidInviteCountsForJob(int jobId)
        {
            var counts = await _context.TradePackageBidInvites
                .Where(i => i.JobId == jobId)
                .GroupBy(i => i.TradePackageId)
                .Select(g => new { tradePackageId = g.Key, invitedCount = g.Count() })
                .ToListAsync();

            return Ok(counts);
        }

        [HttpGet("public")]
        public async Task<ActionResult<IEnumerable<object>>> GetPublicMarketplacePostings()
        {
            try
            {
                var postings = await _context
                    .TradePackages.Where(tp =>
                        tp.PostedToMarketplace
                        && tp.ArchivedAt == null
                        && !tp.IsHidden
                        && !tp.IsInactive
                        && !tp.IsInHouse
                        && tp.Job != null
                        && tp.Job.ArchivedAt == null
                    )
                    .Include(tp => tp.Job)
                        .ThenInclude(j => j.JobAddress)
                    .ToListAsync();

                var userIds = postings
                    .Select(p => p.Job != null ? p.Job.UserId : null)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct()
                    .ToList();

                var users = await _context.Users
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id);

                var ratingsByUser = await _context.Ratings
                    .Where(r => userIds.Contains(r.RatedUserId))
                    .GroupBy(r => r.RatedUserId)
                    .Select(g => new { UserId = g.Key, Avg = g.Average(r => r.RatingValue) })
                    .ToDictionaryAsync(g => g.UserId, g => g.Avg);

                var result = postings
                    .Select(tp =>
                    {
                        var job = tp.Job;
                        var address = job?.JobAddress;

                        var displayBudget = tp.EffectiveBudget > 0
                            ? tp.EffectiveBudget
                            : (tp.TotalBudget > 0 ? tp.TotalBudget : tp.Budget);

                        var displayEstimatedManHours = tp.EstimatedManHours > 0
                            ? tp.EstimatedManHours
                            : (
                                tp.LaborBudget > 0 && tp.HourlyRate > 0
                                    ? Math.Round(tp.LaborBudget / tp.HourlyRate, 2)
                                    : 0
                            );

                        var displayEstimatedDuration = !string.IsNullOrWhiteSpace(tp.EstimatedDuration)
                            ? tp.EstimatedDuration
                            : "TBD";

                        var user = job != null && !string.IsNullOrWhiteSpace(job.UserId)
                            ? users.GetValueOrDefault(job.UserId)
                            : null;
                        var clientRating = job != null && !string.IsNullOrWhiteSpace(job.UserId)
                            ? ratingsByUser.GetValueOrDefault(job.UserId, 0)
                            : 0;

                        var formattedAddress = job?.Address
                            ?? (address != null ? address.FormattedAddress : "");

                        return new
                        {
                            tradePackageId = tp.Id,
                            jobId = tp.JobId,
                            projectName = job != null ? job.ProjectName : string.Empty,
                            jobType = !string.IsNullOrWhiteSpace(tp.Category)
                                ? tp.Category
                                : (job != null ? job.JobType : string.Empty),
                            status = !string.IsNullOrWhiteSpace(tp.Status) ? tp.Status : "Posted",
                            address = formattedAddress,
                            streetNumber = address != null ? address.StreetNumber : string.Empty,
                            streetName = address != null ? address.StreetName : string.Empty,
                            city = address != null ? address.City : string.Empty,
                            state = address != null ? address.State : string.Empty,
                            postalCode = address != null ? address.PostalCode : string.Empty,
                            country = address != null ? address.Country : string.Empty,
                            latitude = address != null && address.Latitude.HasValue
                                ? address.Latitude.Value.ToString()
                                : "0",
                            longitude = address != null && address.Longitude.HasValue
                                ? address.Longitude.Value.ToString()
                                : "0",
                            googlePlaceId = address != null ? address.GooglePlaceId : string.Empty,
                            description = tp.ScopeOfWork ?? string.Empty,
                            title = tp.TradeName,
                            biddingType = tp.LaborType ?? "Labor and Materials",
                            trades = new[] { tp.TradeName },
                            tradeBudgets = new[]
                            {
                                new
                                {
                                    tradeName = tp.TradeName,
                                    budget = (double)displayBudget,
                                },
                            },
                            potentialStartDate = tp.StartDate,
                            biddingStartDate = tp.BidDeadline ?? tp.CreatedAt,
                            createdAt = tp.CreatedAt,
                            clientName = user != null ? $"{user.FirstName} {user.LastName}" : string.Empty,
                            clientCompanyName = user?.CompanyName,
                            clientRating = clientRating,
                            tradePackageLaborBudgetVisible = tp.LaborBudgetVisible,
                            tradePackageMaterialBudgetVisible = tp.MaterialBudgetVisible,
                            tradePackageEstimatedManHours = displayEstimatedManHours,
                            tradePackageEstimatedDuration = displayEstimatedDuration,
                        };
                    })
                    .OrderByDescending(r => r.createdAt)
                    .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to fetch public marketplace postings", details = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTradePackage(int id, TradePackage tradePackage)
        {
            if (id != tradePackage.Id)
            {
                return BadRequest();
            }

            var existing = await _context.TradePackages.FirstOrDefaultAsync(tp => tp.Id == id);
            if (existing == null)
            {
                return NotFound();
            }

            existing.TradeName = tradePackage.TradeName;
            existing.Category = tradePackage.Category;
            existing.ScopeOfWork = tradePackage.ScopeOfWork;
            existing.Status = tradePackage.Status;
            existing.EstimatedManHours = tradePackage.EstimatedManHours;
            existing.HourlyRate = tradePackage.HourlyRate;
            existing.EstimatedDuration = tradePackage.EstimatedDuration;
            existing.StartDate = tradePackage.StartDate;
            existing.BidDeadline = tradePackage.BidDeadline;
            existing.LaborType = tradePackage.LaborType;
            existing.CsiCode = tradePackage.CsiCode;
            existing.LinkedTradePackageId = tradePackage.LinkedTradePackageId;
            existing.IsAutoGenerated = tradePackage.IsAutoGenerated;
            existing.IsInactive = tradePackage.IsInactive;
            existing.IsHidden = tradePackage.IsHidden;
            existing.SourceType = tradePackage.SourceType;
            existing.IsInHouse = tradePackage.IsInHouse;
            existing.Notes = tradePackage.Notes;
            existing.LaborBudgetVisible = tradePackage.LaborBudgetVisible;
            existing.MaterialBudgetVisible = tradePackage.MaterialBudgetVisible;

            existing.LaborBudget = tradePackage.LaborBudget;
            existing.MaterialBudget = tradePackage.MaterialBudget;
            existing.TotalBudget = tradePackage.TotalBudget;
            existing.EffectiveBudget = tradePackage.EffectiveBudget;

            ApplyBudgetRules(existing);

            // Never allow system-generated supplier packages to be posted directly
            if (IsSupplier(existing) && existing.IsAutoGenerated)
            {
                existing.PostedToMarketplace = false;
                existing.Status = string.IsNullOrWhiteSpace(existing.Status)
                    ? "Draft"
                    : existing.Status;
            }
            else if (existing.IsInHouse)
            {
                existing.PostedToMarketplace = false;
                existing.Status = string.IsNullOrWhiteSpace(tradePackage.Status)
                    ? "In House"
                    : tradePackage.Status;
            }
            else
            {
                existing.PostedToMarketplace = tradePackage.PostedToMarketplace;
            }

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

        [HttpPost("{id}/bid-invites")]
        public async Task<IActionResult> SaveBidInvites(int id, [FromBody] SaveTradePackageBidInvitesRequestDto request)
        {
            if (request == null || request.JobId <= 0 || request.TradePackageId <= 0)
            {
                return BadRequest("jobId and tradePackageId are required.");
            }

            if (request.TradePackageId != id)
            {
                return BadRequest("TradePackageId does not match route id.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = User.FindFirstValue("UserId");

            var tradePackage = await _context.TradePackages.FirstOrDefaultAsync(tp =>
                tp.Id == request.TradePackageId && tp.JobId == request.JobId
            );
            if (tradePackage == null)
            {
                return NotFound("Trade package not found for the provided job.");
            }

            foreach (var row in request.Invitees)
            {
                var email = (row.Email ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(email))
                {
                    continue;
                }

                var existing = await _context.TradePackageBidInvites.FirstOrDefaultAsync(i =>
                    i.TradePackageId == request.TradePackageId && i.Email == email
                );

                if (existing == null)
                {
                    existing = new TradePackageBidInvite
                    {
                        JobId = request.JobId,
                        TradePackageId = request.TradePackageId,
                        Email = email,
                        CreatedAt = DateTime.UtcNow,
                        CreatedByUserId = userId,
                        Status = "Selected",
                    };
                    _context.TradePackageBidInvites.Add(existing);
                }

                existing.ExternalCompanyId = row.ExternalCompanyId;
                existing.ExternalContactId = row.ExternalContactId;
                existing.ContactName = row.ContactName;
                existing.CompanyName = row.CompanyName;
            }

            await _context.SaveChangesAsync();

            var saved = await _context.TradePackageBidInvites
                .Where(i => i.TradePackageId == request.TradePackageId)
                .ToListAsync();

            return Ok(saved);
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

            if (tradePackage.IsInactive || tradePackage.IsHidden)
            {
                return BadRequest(new { message = "Cannot post inactive or hidden packages." });
            }

            if (tradePackage.IsInHouse)
            {
                return BadRequest(
                    new { message = "In-house packages cannot be posted to marketplace." }
                );
            }

            ApplyBudgetRules(tradePackage);

            tradePackage.PostedToMarketplace = true;
            tradePackage.Status = "Posted";
            await _context.SaveChangesAsync();

            return Ok(new { message = "Package posted to marketplace" });
        }

        [HttpPost("{id}/sync-labor-type")]
        public async Task<IActionResult> SyncLaborTypeAndSupplierLink(
            int id,
            [FromBody] SyncLaborTypeRequest request
        )
        {
            var tradePackage = await _context.TradePackages.FirstOrDefaultAsync(tp => tp.Id == id);
            if (tradePackage == null)
            {
                return NotFound();
            }

            tradePackage.LaborType = request.LaborType;

            if (request.TotalBudget.HasValue && request.TotalBudget.Value > 0)
            {
                tradePackage.TotalBudget = request.TotalBudget.Value;
            }

            if (request.LaborBudget.HasValue && request.LaborBudget.Value >= 0)
            {
                tradePackage.LaborBudget = request.LaborBudget.Value;
            }

            ApplyBudgetRules(tradePackage);

            // Ensure material component is always derived from total-labor for the source trade package
            if (tradePackage.TotalBudget <= 0)
            {
                tradePackage.TotalBudget = tradePackage.Budget;
            }

            var derivedMaterial = Math.Max(
                0,
                Math.Round(tradePackage.TotalBudget - tradePackage.LaborBudget, 2)
            );
            if (derivedMaterial > 0 || tradePackage.MaterialBudget <= 0)
            {
                tradePackage.MaterialBudget = derivedMaterial;
            }

            ApplyBudgetRules(tradePackage);

            var linkedSupplier = tradePackage.LinkedTradePackageId.HasValue
                ? await _context.TradePackages.FirstOrDefaultAsync(tp =>
                    tp.Id == tradePackage.LinkedTradePackageId
                )
                : null;

            var isLaborOnly = IsLaborOnly(tradePackage);
            if (isLaborOnly)
            {
                if (linkedSupplier == null)
                {
                    linkedSupplier = new TradePackage
                    {
                        JobId = tradePackage.JobId,
                        TradeName = $"{tradePackage.TradeName} Materials",
                        Category = "Supplier",
                        ScopeOfWork = tradePackage.ScopeOfWork,
                        Status = "Draft",
                        LaborType = "Labor and Materials",
                        CsiCode = tradePackage.CsiCode,
                        EstimatedManHours = 0,
                        HourlyRate = 0,
                        LaborBudget = 0,
                        MaterialBudget = Math.Max(0, tradePackage.MaterialBudget),
                        TotalBudget = Math.Max(0, tradePackage.MaterialBudget),
                        EffectiveBudget = Math.Max(0, tradePackage.MaterialBudget),
                        Budget = Math.Max(0, tradePackage.MaterialBudget),
                        Notes = tradePackage.Notes,
                        LaborBudgetVisible = false,
                        MaterialBudgetVisible = true,
                        IsAutoGenerated = true,
                        IsInactive = false,
                        IsHidden = false,
                        SourceType = "SYSTEM_LINKED",
                        IsInHouse = false,
                        PostedToMarketplace = false,
                        CreatedAt = DateTime.UtcNow,
                    };

                    _context.TradePackages.Add(linkedSupplier);
                    await _context.SaveChangesAsync();
                    tradePackage.LinkedTradePackageId = linkedSupplier.Id;
                }
                else
                {
                    linkedSupplier.TradeName = $"{tradePackage.TradeName} Materials";
                    linkedSupplier.ScopeOfWork = tradePackage.ScopeOfWork;
                    linkedSupplier.Category = "Supplier";
                    linkedSupplier.Status = "Draft";
                    linkedSupplier.PostedToMarketplace = false;
                    linkedSupplier.IsAutoGenerated = true;
                    linkedSupplier.IsInactive = false;
                    linkedSupplier.IsHidden = false;
                    linkedSupplier.SourceType = "SYSTEM_LINKED";
                    linkedSupplier.IsInHouse = false;
                    linkedSupplier.CsiCode = tradePackage.CsiCode;
                    linkedSupplier.LaborType = "Labor and Materials";
                    linkedSupplier.LaborBudget = 0;
                    linkedSupplier.MaterialBudget = Math.Max(0, tradePackage.MaterialBudget);
                    linkedSupplier.TotalBudget = Math.Max(0, tradePackage.MaterialBudget);
                    linkedSupplier.EffectiveBudget = Math.Max(0, tradePackage.MaterialBudget);
                    linkedSupplier.Budget = Math.Max(0, tradePackage.MaterialBudget);
                    linkedSupplier.Notes = tradePackage.Notes;
                    linkedSupplier.LaborBudgetVisible = false;
                    linkedSupplier.MaterialBudgetVisible = true;
                }
            }
            else if (linkedSupplier != null)
            {
                linkedSupplier.IsInactive = true;
                linkedSupplier.IsHidden = true;
                linkedSupplier.PostedToMarketplace = false;
                if (string.IsNullOrWhiteSpace(linkedSupplier.Status))
                {
                    linkedSupplier.Status = "Draft";
                }
            }

            await _context.SaveChangesAsync();

            return Ok(
                new
                {
                    message = "Labor type synchronized",
                    linkedTradePackageId = tradePackage.LinkedTradePackageId,
                }
            );
        }

        [HttpPost("simple")]
        [RequestSizeLimit(50 * 1024 * 1024)] // 50MB for blueprint
        public async Task<IActionResult> PostSimpleJob([FromForm] SimpleJobDto jobRequest)
        {
            if (jobRequest == null)
            {
                return BadRequest("Job request cannot be null.");
            }

            if (
                string.IsNullOrWhiteSpace(jobRequest.FullName)
                || string.IsNullOrWhiteSpace(jobRequest.Email)
                || string.IsNullOrWhiteSpace(jobRequest.JobDescription)
            )
            {
                return BadRequest("Full name, email, and job description are required.");
            }

            // Validate trades if tradesman is selected
            if (
                jobRequest.ProfessionalType == "tradesman"
                && (jobRequest.SelectedTrades == null || jobRequest.SelectedTrades.Count == 0)
            )
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
                            isPlaceholder = true,
                        };

                        var result = await _userManager.CreateAsync(
                            placeholderUser,
                            PasswordGenerator.GenerateRandomPassword(12)
                        );

                        if (!result.Succeeded)
                        {
                            return BadRequest(
                                $"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}"
                            );
                        }

                        userId = placeholderUser.Id;
                    }

                    // 2. Create the job
                    var projectTypeName =
                        jobRequest.ProjectType == "new-build" ? "New Build" : "Renovation";

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
                        var extension = Path.GetExtension(jobRequest.BlueprintFile.FileName)
                            .ToLowerInvariant();

                        if (!allowedExtensions.Contains(extension))
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(
                                new
                                {
                                    error = $"Invalid file type: {jobRequest.BlueprintFile.FileName}. Allowed types: PDF, PNG, JPG, JPEG",
                                }
                            );
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
                            LaborBudget = 0,
                            MaterialBudget = 0,
                            TotalBudget = 0,
                            EffectiveBudget = 0,
                            Status = "Posted",
                            EstimatedManHours = 0,
                            HourlyRate = 0,
                            LaborType = "Labor and Materials",
                            SourceType = "USER",
                            IsInHouse = false,
                            PostedToMarketplace = true,
                            Notes = null,
                            LaborBudgetVisible = true,
                            MaterialBudgetVisible = true,
                            CreatedAt = DateTime.UtcNow,
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
                                LaborBudget = 0,
                                MaterialBudget = 0,
                                TotalBudget = 0,
                                EffectiveBudget = 0,
                                Status = "Posted",
                                EstimatedManHours = 0,
                                HourlyRate = 0,
                                LaborType = "Labor and Materials",
                                SourceType = "USER",
                                IsInHouse = false,
                                PostedToMarketplace = true,
                                Notes = null,
                                LaborBudgetVisible = true,
                                MaterialBudgetVisible = true,
                                CreatedAt = DateTime.UtcNow,
                            };
                            tradePackages.Add(tradePackage);
                        }
                    }

                    _context.TradePackages.AddRange(tradePackages);
                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();

                    return Ok(
                        new
                        {
                            success = true,
                            jobId = job.Id,
                            tradePackageIds = tradePackages.Select(tp => tp.Id).ToList(),
                            userId = userId,
                            isNewUser = existingUser == null,
                            blueprintUrl = uploadedFileUrls.FirstOrDefault(),
                        }
                    );
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
                var assignedJobIds = await _context
                    .JobAssignments.Where(ja => ja.UserId == userId)
                    .Select(ja => ja.JobId)
                    .Distinct()
                    .ToListAsync();

                var postings = await _context
                    .TradePackages.Where(tp =>
                        (tp.Job.UserId == userId || assignedJobIds.Contains(tp.JobId))
                        && tp.PostedToMarketplace
                        && tp.ArchivedAt == null
                        && !tp.IsHidden
                        && !tp.IsInactive
                    )
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
                        tp.LaborBudget,
                        tp.MaterialBudget,
                        tp.TotalBudget,
                        tp.EffectiveBudget,
                        tp.EstimatedDuration,
                        tp.StartDate,
                        tp.BidDeadline,
                        tp.LaborType,
                        tp.CsiCode,
                        tp.LinkedTradePackageId,
                        tp.IsAutoGenerated,
                        tp.IsInactive,
                        tp.IsHidden,
                        tp.SourceType,
                        tp.IsInHouse,
                        tp.PostedToMarketplace,
                        tp.Notes,
                        tp.LaborBudgetVisible,
                        tp.MaterialBudgetVisible,
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

        private static bool IsSupplier(TradePackage tradePackage)
        {
            return string.Equals(
                tradePackage.Category,
                "Supplier",
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static bool IsLaborOnly(TradePackage tradePackage)
        {
            var normalized = (tradePackage.LaborType ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "labor" || normalized == "labor only";
        }

        private static void ApplyBudgetRules(TradePackage tradePackage)
        {
            var computedLaborBudget = Math.Round(
                tradePackage.EstimatedManHours * tradePackage.HourlyRate,
                2
            );
            if (tradePackage.LaborBudget <= 0)
            {
                tradePackage.LaborBudget = computedLaborBudget;
            }

            if (tradePackage.MaterialBudget < 0)
            {
                tradePackage.MaterialBudget = 0;
            }

            var incomingTotal = Math.Max(0, tradePackage.TotalBudget);
            var incomingBudget = Math.Max(0, tradePackage.Budget);

            if (incomingTotal <= 0)
            {
                incomingTotal =
                    incomingBudget > 0
                        ? incomingBudget
                        : (tradePackage.LaborBudget + tradePackage.MaterialBudget);
            }

            if (incomingTotal < tradePackage.LaborBudget)
            {
                incomingTotal = tradePackage.LaborBudget;
            }

            tradePackage.TotalBudget = Math.Round(incomingTotal, 2);
            tradePackage.MaterialBudget = Math.Round(
                Math.Max(0, tradePackage.TotalBudget - tradePackage.LaborBudget),
                2
            );

            var effective = IsLaborOnly(tradePackage)
                ? tradePackage.LaborBudget
                : tradePackage.TotalBudget;

            tradePackage.EffectiveBudget = Math.Round(effective, 2);
            tradePackage.Budget = tradePackage.EffectiveBudget;
        }

        public class SyncLaborTypeRequest
        {
            public string? LaborType { get; set; }

            public decimal? TotalBudget { get; set; }

            public decimal? LaborBudget { get; set; }
        }
    }
}
