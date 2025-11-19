using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;

namespace ProbuildBackend.Services
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
                .FirstOrDefaultAsync(t => t.TemplateName == templateName && t.IsActive);

        }
    }
}