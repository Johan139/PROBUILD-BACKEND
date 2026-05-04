namespace BuildigBackend.Models.DTO;

public class ReferralLinkDto
{
    public string Code { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class ReferralResolveDto
{
    public bool Valid { get; set; }
    public string? ReferrerUserId { get; set; }
    public string? Message { get; set; }
}
