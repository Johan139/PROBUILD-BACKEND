using Microsoft.EntityFrameworkCore;
using BuildigBackend.Interface;
using BuildigBackend.Models;

namespace BuildigBackend.Services
{
    public class EmailTemplateService : IEmailTemplateService
    {
        private readonly ApplicationDbContext _context;

        public EmailTemplateService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<EmailTemplate> GetTemplateAsync(string templateName)
        {
            return await _context.EmailTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TemplateName == templateName && t.IsActive);
        }
    }
}

