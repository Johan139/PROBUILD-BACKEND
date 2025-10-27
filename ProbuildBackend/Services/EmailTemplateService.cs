using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using System;

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