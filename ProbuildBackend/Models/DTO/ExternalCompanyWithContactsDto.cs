namespace ProbuildBackend.Models.DTO
{
    public class ExternalCompanyWithContactsDto
    {
        public ExternalCompanyDto Company { get; set; } = new();
        public List<ExternalContactDto> Contacts { get; set; } = new();
    }
}
