using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Options;

namespace ProbuildBackend.Services
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

            var searchKeywords = BuildSearchKeywords(request);

            var payload = new
            {
                q_keywords = searchKeywords,
                page = 1,
                per_page = normalizedLimit,
            };

            JsonDocument? searchDoc = null;

            try
            {
                searchDoc = await PostApolloAsync(
                    "/mixed_companies/search",
                    payload,
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Apollo search call failed for {Keywords}", searchKeywords);
            }

            var companiesFromSearch = searchDoc is null
                ? new List<ExternalCompanyWithContactsDto>()
                : await ParseAndPersistSearchResultsAsync(searchDoc, cancellationToken);

            if (companiesFromSearch.Count == 0)
            {
                return await QueryCachedByTradeAndLocationAsync(
                    request,
                    normalizedLimit,
                    cancellationToken
                );
            }

            return companiesFromSearch;
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

        private async Task<List<ExternalCompanyWithContactsDto>> QueryCachedByTradeAndLocationAsync(
            SubcontractorDiscoveryRequestDto request,
            int limit,
            CancellationToken cancellationToken
        )
        {
            var trade = request.TradeName.Trim();
            var city = request.City?.Trim();
            var state = request.State?.Trim();

            var query = _context
                .ExternalCompanies.Include(c => c.Contacts)
                .Where(c => c.Source == "Apollo")
                .AsQueryable();

            query = query.Where(c =>
                (c.Industry != null && c.Industry.Contains(trade))
                || (c.Description != null && c.Description.Contains(trade))
                || c.Name.Contains(trade)
            );

            if (!string.IsNullOrWhiteSpace(city))
            {
                query = query.Where(c => c.City != null && c.City == city);
            }

            if (!string.IsNullOrWhiteSpace(state))
            {
                query = query.Where(c => c.State != null && c.State == state);
            }

            var cached = await query
                .OrderByDescending(c => c.LastEnrichedAt)
                .ThenByDescending(c => c.UpdatedAt)
                .Take(limit)
                .ToListAsync(cancellationToken);

            return cached.Select(MapToWithContactsDto).ToList();
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
                Source = company.Source,
                ExternalId = company.ExternalId,
                Name = company.Name,
                Domain = company.Domain,
                WebsiteUrl = company.WebsiteUrl,
                LinkedinUrl = company.LinkedinUrl,
                Phone = company.Phone,
                City = company.City,
                State = company.State,
                Country = company.Country,
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
                Source = contact.Source,
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
