using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using BuildigBackend.Interface;
using BuildigBackend.Models;
using BuildigBackend.Models.DTO;
using BuildigBackend.Options;

namespace BuildigBackend.Services
{
    public class ApolloService : IApolloService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ApolloOptions _options;
        private readonly ILogger<ApolloService> _logger;

        public ApolloService(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IOptions<ApolloOptions> options,
            ILogger<ApolloService> logger
        )
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<List<ExternalCompanyWithContactsDto>> DiscoverSubcontractorsAsync(
            SubcontractorDiscoveryRequestDto request,
            CancellationToken cancellationToken = default
        )
        {
            var normalizedLimit = Math.Clamp(
                request.Limit <= 0 ? _options.DefaultSearchLimit : request.Limit,
                1,
                30
            );

            var cachedFirst = await QueryCachedRankedAsync(
                request,
                normalizedLimit,
                cancellationToken
            );
            return cachedFirst;
        }

        public async Task<ExternalCompanyWithContactsDto?> EnrichGeneralContractorAsync(
            GeneralContractorEnrichRequestDto request,
            CancellationToken cancellationToken = default
        )
        {
            var normalizedDomain = NormalizeDomain(request.Domain);

            if (!string.IsNullOrWhiteSpace(normalizedDomain))
            {
                var cached = await _context
                    .ExternalCompanies.Include(c => c.Contacts)
                    .FirstOrDefaultAsync(
                        c => c.Source == "Apollo" && c.Domain == normalizedDomain,
                        cancellationToken
                    );

                if (cached is not null && cached.LastEnrichedAt.HasValue)
                {
                    var age = DateTime.UtcNow - cached.LastEnrichedAt.Value;
                    if (age < TimeSpan.FromDays(30))
                    {
                        return MapToWithContactsDto(cached);
                    }
                }
            }

            JsonDocument? enrichDoc = null;

            try
            {
                var query = !string.IsNullOrWhiteSpace(normalizedDomain)
                    ? $"domain={Uri.EscapeDataString(normalizedDomain)}"
                    : $"organization_name={Uri.EscapeDataString(request.CompanyName)}";

                enrichDoc = await GetApolloAsync(
                    $"/organizations/enrich?{query}",
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Apollo enrich call failed for company {CompanyName}, domain {Domain}",
                    request.CompanyName,
                    normalizedDomain
                );
            }

            if (enrichDoc is null)
            {
                return await FallbackCompanyLookupAsync(
                    request.CompanyName,
                    normalizedDomain,
                    cancellationToken
                );
            }

            var enriched = await ParseAndPersistEnrichmentAsync(enrichDoc, cancellationToken);
            if (enriched is not null)
            {
                return enriched;
            }

            return await FallbackCompanyLookupAsync(
                request.CompanyName,
                normalizedDomain,
                cancellationToken
            );
        }

        private async Task<List<ExternalCompanyWithContactsDto>> ParseAndPersistSearchResultsAsync(
            JsonDocument doc,
            CancellationToken cancellationToken
        )
        {
            var results = new List<ExternalCompanyWithContactsDto>();
            var root = doc.RootElement;

            var companyNodes = GetArrayCandidates(root, "organizations", "companies", "accounts");
            foreach (var companyNode in companyNodes)
            {
                var company = await UpsertExternalCompanyAsync(companyNode, cancellationToken);
                if (company is null)
                {
                    continue;
                }

                var contacts = await UpsertContactsFromNodeAsync(
                    company.Id,
                    companyNode,
                    cancellationToken
                );
                results.Add(
                    new ExternalCompanyWithContactsDto
                    {
                        Company = MapToDto(company),
                        Contacts = contacts.Select(MapToDto).ToList(),
                    }
                );
            }

            if (results.Count == 0)
            {
                var singleCompany = await UpsertExternalCompanyAsync(root, cancellationToken);
                if (singleCompany is not null)
                {
                    var contacts = await UpsertContactsFromNodeAsync(
                        singleCompany.Id,
                        root,
                        cancellationToken
                    );

                    results.Add(
                        new ExternalCompanyWithContactsDto
                        {
                            Company = MapToDto(singleCompany),
                            Contacts = contacts.Select(MapToDto).ToList(),
                        }
                    );
                }
            }

            return results;
        }

        private async Task<ExternalCompanyWithContactsDto?> ParseAndPersistEnrichmentAsync(
            JsonDocument doc,
            CancellationToken cancellationToken
        )
        {
            var root = doc.RootElement;
            var orgNode =
                GetFirstObjectCandidate(root, "organization", "company", "account") ?? root;
            var company = await UpsertExternalCompanyAsync(orgNode, cancellationToken);
            if (company is null)
            {
                return null;
            }

            var contacts = await UpsertContactsFromNodeAsync(company.Id, root, cancellationToken);
            company.LastEnrichedAt = DateTime.UtcNow;
            company.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            return new ExternalCompanyWithContactsDto
            {
                Company = MapToDto(company),
                Contacts = contacts.Select(MapToDto).ToList(),
            };
        }

        private async Task<List<ExternalCompanyWithContactsDto>> QueryCachedRankedAsync(
            SubcontractorDiscoveryRequestDto request,
            int limit,
            CancellationToken cancellationToken
        )
        {
            var trade = (request.TradeName ?? string.Empty).Trim();
            var city = request.City?.Trim();
            var state = request.State?.Trim();
            var searchText = request.SearchText?.Trim();

            decimal? jobLat = null;
            decimal? jobLng = null;
            if (request.JobId.HasValue)
            {
                var job = await _context.Jobs
                    .Include(j => j.JobAddress)
                    .FirstOrDefaultAsync(j => j.Id == request.JobId.Value, cancellationToken);
                jobLat = job?.JobAddress?.Latitude;
                jobLng = job?.JobAddress?.Longitude;
            }

            var keywords = BuildTradeKeywords(trade, searchText);
            if (keywords.Count == 0)
            {
                keywords.AddRange(ExtractTokens(trade));
            }

            var query = _context.ExternalCompanies.Include(c => c.Contacts).AsQueryable();

            query = query.Where(c =>
                (c.Email != null && c.Email != "")
                || c.Contacts.Any(ct => ct.Email != null && ct.Email != "")
            );

            query = query.Where(c =>
                (c.Email != null && c.Email != "")
                && c.EmailConfidence != null
                && c.EmailConfidence == "High"
            );

            if (!string.IsNullOrWhiteSpace(city))
            {
                query = query.Where(c => c.City != null && c.City == city);
            }

            var hasJobCoordinates = jobLat.HasValue && jobLng.HasValue;

            if (!hasJobCoordinates && !string.IsNullOrWhiteSpace(state))
            {
                query = query.Where(c => c.State != null && c.State == state);
            }

            // Coarse filter in SQL to keep the dataset small, then do proper scoring in-memory.
            if (keywords.Count > 0)
            {
                query = query.Where(c =>
                    keywords.Any(k =>
                        (c.Name != null && c.Name.Contains(k))
                        || (c.Industry != null && c.Industry.Contains(k))
                        || (c.Description != null && c.Description.Contains(k))
                        || (c.Domain != null && c.Domain.Contains(k))
                    )
                );
            }

            var fetchCount = Math.Clamp(limit * 8, 25, 300);
            var candidates = await query
                .OrderByDescending(c => c.LastEnrichedAt)
                .ThenByDescending(c => c.UpdatedAt)
                .Take(fetchCount)
                .ToListAsync(cancellationToken);

            if (jobLat.HasValue && jobLng.HasValue)
            {
                candidates = candidates
                    .Where(c =>
                        c.Latitude.HasValue
                        && c.Longitude.HasValue
                        && HaversineDistanceKm(jobLat.Value, jobLng.Value, c.Latitude.Value, c.Longitude.Value) <= 100d
                    )
                    .ToList();
            }

            var ranked = candidates
                .Select(c => new { Company = c, Score = ScoreCompany(c, trade, keywords) })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Company.LastEnrichedAt)
                .ThenByDescending(x => x.Company.UpdatedAt)
                .Take(limit)
                .Select(x => MapToWithContactsDto(x.Company))
                .ToList();

            return ranked;
        }

        private static List<string> BuildTradeKeywords(string tradeName, string? searchText)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in ExtractTokens(tradeName))
            {
                tokens.Add(t);
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                foreach (var t in ExtractTokens(searchText))
                {
                    tokens.Add(t);
                }
            }

            // Expand common construction synonyms.
            var normalized = NormalizeForMatch(tradeName);
            if (normalized.Contains("excavat"))
            {
                tokens.Add("excavation");
                tokens.Add("excavating");
                tokens.Add("earthwork");
                tokens.Add("grading");
                tokens.Add("sitework");
            }
            if (normalized.Contains("concret"))
            {
                tokens.Add("concrete");
                tokens.Add("cement");
                tokens.Add("flatwork");
                tokens.Add("foundation");
            }
            if (normalized.Contains("plumb"))
            {
                tokens.Add("plumbing");
                tokens.Add("pipe");
                tokens.Add("piping");
            }
            if (normalized.Contains("electric"))
            {
                tokens.Add("electrical");
                tokens.Add("electrician");
                tokens.Add("wiring");
            }
            if (normalized.Contains("hvac") || normalized.Contains("heating") || normalized.Contains("cooling"))
            {
                tokens.Add("hvac");
                tokens.Add("heating");
                tokens.Add("cooling");
                tokens.Add("air conditioning");
                tokens.Add("ventilation");
            }
            if (normalized.Contains("roof"))
            {
                tokens.Add("roofing");
                tokens.Add("roofer");
                tokens.Add("waterproofing");
            }
            if (normalized.Contains("paint"))
            {
                tokens.Add("painting");
                tokens.Add("painter");
            }

            return tokens
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> ExtractTokens(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<string>();
            }

            var cleaned = NormalizeForMatch(value);
            var parts = Regex.Split(cleaned, "[^a-z0-9]+").Where(p => p.Length >= 3);

            var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "and",
                "the",
                "for",
                "with",
                "service",
                "services",
                "company",
                "co",
                "inc",
                "llc",
                "ltd",
                "contractor",
                "contractors",
                "construction",
            };

            return parts.Where(p => !stop.Contains(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string NormalizeForMatch(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim().ToLowerInvariant();
        }

        private static int ScoreCompany(ExternalCompany company, string tradeName, List<string> keywords)
        {
            var text = NormalizeForMatch(
                string.Join(
                    ' ',
                    new[]
                    {
                        company.Name,
                        company.Industry,
                        company.Description,
                        company.Domain,
                    }.Where(x => !string.IsNullOrWhiteSpace(x))
                )
            );

            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var score = 0;
            var tradeNorm = NormalizeForMatch(tradeName);

            if (!string.IsNullOrWhiteSpace(tradeNorm) && text.Contains(tradeNorm))
            {
                score += 50;
            }

            foreach (var k in keywords)
            {
                var kn = NormalizeForMatch(k);
                if (string.IsNullOrWhiteSpace(kn)) continue;

                if (text.Contains(kn))
                {
                    score += 12;
                    if (!string.IsNullOrWhiteSpace(company.Name) && NormalizeForMatch(company.Name).Contains(kn))
                    {
                        score += 8;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(company.City)) score += 2;
            if (!string.IsNullOrWhiteSpace(company.State)) score += 2;
            if (!string.IsNullOrWhiteSpace(company.WebsiteUrl) || !string.IsNullOrWhiteSpace(company.Domain)) score += 2;

            return score;
        }

        private async Task<ExternalCompanyWithContactsDto?> FallbackCompanyLookupAsync(
            string companyName,
            string? normalizedDomain,
            CancellationToken cancellationToken
        )
        {
            var query = _context.ExternalCompanies.Include(c => c.Contacts).AsQueryable();

            if (!string.IsNullOrWhiteSpace(normalizedDomain))
            {
                query = query.Where(c => c.Domain == normalizedDomain);
            }
            else
            {
                query = query.Where(c => c.Name == companyName);
            }

            var company = await query
                .OrderByDescending(c => c.LastEnrichedAt)
                .ThenByDescending(c => c.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            return company is null ? null : MapToWithContactsDto(company);
        }

        private async Task<ExternalCompany?> UpsertExternalCompanyAsync(
            JsonElement node,
            CancellationToken cancellationToken
        )
        {
            var externalId = GetString(node, "id", "organization_id", "company_id");
            var name = GetString(node, "name", "organization_name", "company_name");
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var domain = NormalizeDomain(GetString(node, "domain", "primary_domain"));
            var website = GetString(node, "website_url", "website");
            if (string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(website))
            {
                domain = NormalizeDomain(website);
            }

            ExternalCompany? company = null;

            if (!string.IsNullOrWhiteSpace(externalId))
            {
                company = await _context.ExternalCompanies.FirstOrDefaultAsync(
                    c => c.Source == "Apollo" && c.ExternalId == externalId,
                    cancellationToken
                );
            }

            if (company is null)
            {
                company = await _context.ExternalCompanies.FirstOrDefaultAsync(
                    c => c.Source == "Apollo" && c.Name == name && c.Domain == domain,
                    cancellationToken
                );
            }

            if (company is null)
            {
                company = new ExternalCompany
                {
                    Source = "Apollo",
                    ExternalId = externalId,
                    Name = name,
                    Domain = domain,
                    CreatedAt = DateTime.UtcNow,
                };
                _context.ExternalCompanies.Add(company);
            }

            company.ExternalId = externalId ?? company.ExternalId;
            company.Name = name;
            company.Domain = domain;
            company.WebsiteUrl = website;
            company.LinkedinUrl = GetString(node, "linkedin_url", "linkedin");
            company.Phone = GetString(node, "phone", "phone_number");
            company.City = GetString(node, "city");
            company.State = GetString(node, "state", "region");
            company.Country = GetString(node, "country");
            company.Latitude = GetDecimal(node, "latitude", "lat", "organization_latitude", "organizationLatitude", "location_latitude", "locationLatitude") ?? company.Latitude;
            company.Longitude = GetDecimal(node, "longitude", "lng", "lon", "organization_longitude", "organizationLongitude", "location_longitude", "locationLongitude") ?? company.Longitude;
            company.Description = GetString(node, "short_description", "description");
            company.Industry = GetString(node, "industry", "industry_category");
            company.EmployeeCount = GetInt(node, "estimated_num_employees", "employee_count");
            company.FoundedYear = GetInt(node, "founded_year", "founded");
            company.LastEnrichedAt = DateTime.UtcNow;
            company.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            return company;
        }

        private async Task<List<ExternalContact>> UpsertContactsFromNodeAsync(
            int companyId,
            JsonElement node,
            CancellationToken cancellationToken
        )
        {
            var results = new List<ExternalContact>();
            var contactNodes = GetArrayCandidates(node, "contacts", "people", "employees");

            foreach (var contactNode in contactNodes)
            {
                var contact = await UpsertExternalContactAsync(
                    companyId,
                    contactNode,
                    cancellationToken
                );
                if (contact is not null)
                {
                    results.Add(contact);
                }
            }

            return results;
        }

        private async Task<ExternalContact?> UpsertExternalContactAsync(
            int companyId,
            JsonElement node,
            CancellationToken cancellationToken
        )
        {
            var externalId = GetString(node, "id", "person_id", "contact_id");
            var email = GetString(node, "email", "email_address");
            var firstName = GetString(node, "first_name", "firstName");
            var lastName = GetString(node, "last_name", "lastName");
            var fullName = GetString(node, "name", "full_name");

            if (string.IsNullOrWhiteSpace(fullName))
            {
                fullName = string.Join(
                    ' ',
                    new[] { firstName, lastName }.Where(x => !string.IsNullOrWhiteSpace(x))
                );
            }

            if (
                string.IsNullOrWhiteSpace(externalId)
                && string.IsNullOrWhiteSpace(email)
                && string.IsNullOrWhiteSpace(fullName)
            )
            {
                return null;
            }

            ExternalContact? existing = null;
            if (!string.IsNullOrWhiteSpace(externalId))
            {
                existing = await _context.ExternalContacts.FirstOrDefaultAsync(
                    c => c.Source == "Apollo" && c.ExternalId == externalId,
                    cancellationToken
                );
            }

            if (existing is null && !string.IsNullOrWhiteSpace(email))
            {
                existing = await _context.ExternalContacts.FirstOrDefaultAsync(
                    c => c.ExternalCompanyId == companyId && c.Email == email,
                    cancellationToken
                );
            }

            if (existing is null)
            {
                existing = new ExternalContact
                {
                    Source = "Apollo",
                    ExternalCompanyId = companyId,
                    CreatedAt = DateTime.UtcNow,
                };
                _context.ExternalContacts.Add(existing);
            }

            existing.ExternalCompanyId = companyId;
            existing.ExternalId = externalId;
            existing.FirstName = firstName;
            existing.LastName = lastName;
            existing.FullName = fullName;
            existing.Title = GetString(node, "title", "job_title");
            existing.Email = email;
            existing.Phone = GetString(node, "phone", "phone_number");
            existing.LinkedinUrl = GetString(node, "linkedin_url", "linkedin");
            existing.Headline = GetString(node, "headline", "description");
            existing.RawPayloadJson = SafeSerialize(node);
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            return existing;
        }

        private async Task<JsonDocument> PostApolloAsync(
            string path,
            object payload,
            CancellationToken cancellationToken
        )
        {
            using var client = CreateApolloClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                ),
            };

            using var response = await client.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(content);
        }

        private async Task<JsonDocument> GetApolloAsync(
            string pathAndQuery,
            CancellationToken cancellationToken
        )
        {
            using var client = CreateApolloClient();
            using var response = await client.GetAsync(pathAndQuery, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(content);
        }

        private HttpClient CreateApolloClient()
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/'));
            client.Timeout = TimeSpan.FromSeconds(
                _options.RequestTimeoutSeconds <= 0 ? 20 : _options.RequestTimeoutSeconds
            );
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    _options.ApiKey
                );
            }

            return client;
        }

        private static string BuildSearchKeywords(SubcontractorDiscoveryRequestDto request)
        {
            var parts = new List<string> { request.TradeName, "subcontractor" };

            if (!string.IsNullOrWhiteSpace(request.SearchText))
            {
                parts.Add(request.SearchText.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.City))
            {
                parts.Add(request.City.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.State))
            {
                parts.Add(request.State.Trim());
            }

            return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static string? NormalizeDomain(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var candidate = value.Trim();
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                return uri.Host.ToLowerInvariant();
            }

            candidate = candidate
                .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim('/');

            return string.IsNullOrWhiteSpace(candidate) ? null : candidate.ToLowerInvariant();
        }

        private static ExternalCompanyDto MapToDto(ExternalCompany company)
        {
            return new ExternalCompanyDto
            {
                Id = company.Id,
                Source = string.IsNullOrWhiteSpace(company.Source) ? "Apollo" : company.Source,
                ExternalId = company.ExternalId,
                Name = company.Name ?? string.Empty,
                Domain = company.Domain,
                WebsiteUrl = company.WebsiteUrl,
                LinkedinUrl = company.LinkedinUrl,
                Email = company.Email,
                EmailConfidence = company.EmailConfidence,
                Phone = company.Phone,
                City = company.City,
                State = company.State,
                Country = company.Country,
                Latitude = company.Latitude,
                Longitude = company.Longitude,
                Description = company.Description,
                Industry = company.Industry,
                EmployeeCount = company.EmployeeCount,
                FoundedYear = company.FoundedYear,
            };
        }

        private static ExternalContactDto MapToDto(ExternalContact contact)
        {
            return new ExternalContactDto
            {
                Id = contact.Id,
                Source = string.IsNullOrWhiteSpace(contact.Source) ? "Apollo" : contact.Source,
                ExternalId = contact.ExternalId,
                ExternalCompanyId = contact.ExternalCompanyId,
                FirstName = contact.FirstName,
                LastName = contact.LastName,
                FullName = contact.FullName,
                Title = contact.Title,
                Email = contact.Email,
                Phone = contact.Phone,
                LinkedinUrl = contact.LinkedinUrl,
            };
        }

        private static ExternalCompanyWithContactsDto MapToWithContactsDto(ExternalCompany company)
        {
            return new ExternalCompanyWithContactsDto
            {
                Company = MapToDto(company),
                Contacts = company.Contacts.Select(MapToDto).ToList(),
            };
        }

        private static List<JsonElement> GetArrayCandidates(
            JsonElement root,
            params string[] propertyNames
        )
        {
            foreach (var propertyName in propertyNames)
            {
                if (
                    root.TryGetProperty(propertyName, out var property)
                    && property.ValueKind == JsonValueKind.Array
                )
                {
                    return property.EnumerateArray().ToList();
                }
            }

            return new List<JsonElement>();
        }

        private static JsonElement? GetFirstObjectCandidate(
            JsonElement root,
            params string[] propertyNames
        )
        {
            foreach (var propertyName in propertyNames)
            {
                if (
                    root.TryGetProperty(propertyName, out var property)
                    && property.ValueKind == JsonValueKind.Object
                )
                {
                    return property;
                }
            }

            return null;
        }

        private static decimal? GetDecimal(JsonElement node, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (!node.TryGetProperty(propertyName, out var property))
                {
                    continue;
                }

                if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
                {
                    return number;
                }

                if (
                    property.ValueKind == JsonValueKind.String
                    && decimal.TryParse(
                        property.GetString(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var parsed
                    )
                )
                {
                    return parsed;
                }
            }

            return null;
        }

        private static double HaversineDistanceKm(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
        {
            const double r = 6371d;
            var dLat = DegreesToRadians((double)(lat2 - lat1));
            var dLon = DegreesToRadians((double)(lon2 - lon1));

            var a =
                Math.Pow(Math.Sin(dLat / 2d), 2d)
                + Math.Cos(DegreesToRadians((double)lat1))
                    * Math.Cos(DegreesToRadians((double)lat2))
                    * Math.Pow(Math.Sin(dLon / 2d), 2d);
            var c = 2d * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
            return r * c;
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * (Math.PI / 180d);
        }

        private static string? GetString(JsonElement node, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (!node.TryGetProperty(propertyName, out var property))
                {
                    continue;
                }

                if (property.ValueKind == JsonValueKind.String)
                {
                    var value = property.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
                else if (property.ValueKind == JsonValueKind.Number)
                {
                    return property.GetRawText();
                }
            }

            return null;
        }

        private static int? GetInt(JsonElement node, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (!node.TryGetProperty(propertyName, out var property))
                {
                    continue;
                }

                if (
                    property.ValueKind == JsonValueKind.Number
                    && property.TryGetInt32(out var number)
                )
                {
                    return number;
                }

                if (
                    property.ValueKind == JsonValueKind.String
                    && int.TryParse(property.GetString(), out var parsed)
                )
                {
                    return parsed;
                }
            }

            return null;
        }

        private static string? SafeSerialize(JsonElement node)
        {
            var json = node.GetRawText();
            if (json.Length <= 4000)
            {
                return json;
            }

            return json[..4000];
        }
    }
}

