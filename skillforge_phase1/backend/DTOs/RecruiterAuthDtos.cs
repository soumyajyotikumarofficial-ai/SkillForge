namespace SkillForge.DTOs;

public class RecruiterRegisterDto
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? CompanyName { get; set; }
    public string? Designation { get; set; }
    public string? PhoneNumber { get; set; }
}

public class RecruiterLoginDto
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}
