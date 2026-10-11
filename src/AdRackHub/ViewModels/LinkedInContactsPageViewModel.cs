using AdRackHub.Models;

namespace AdRackHub.ViewModels;

public class LinkedInContactsPageViewModel
{
    public string? Search { get; set; }
    public string ListType { get; set; } = "all";
    public bool NamedOnly { get; set; } = true;
    public bool MissingOnly { get; set; }
    public List<LinkedInContactRow> Contacts { get; set; } = new();
    public int NamedCount { get; set; }
    public int CompanyOnlyCount { get; set; }
    public int CustomerCount { get; set; }
    public int WithProfileCount { get; set; }
    public int MissingProfileCount { get; set; }
}

public class LinkedInContactRow
{
    public int ContactId { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public CustomerType CustomerType { get; init; }
    public string BusinessName { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Title { get; init; }
    public string? PersonName { get; init; }
    public ContactRole Role { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? PeopleSearchUrl { get; init; }
    public string? CompanySearchUrl { get; init; }
    public string? GoogleSearchUrl { get; init; }
    public string? ProfileUrl { get; init; }
    public bool HasPerson => !string.IsNullOrWhiteSpace(PersonName);
    public bool HasProfile => !string.IsNullOrWhiteSpace(ProfileUrl);
}
