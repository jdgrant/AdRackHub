namespace AdRackHub.Models;

public static class CustomerTypeLabels
{
    public static string Singular(CustomerType type) => type switch
    {
        CustomerType.Customer => "Customer",
        CustomerType.Prospect => "Prospect",
        CustomerType.ExpandedProspect => "Expanded Prospect",
        _ => type.ToString()
    };

    public static string Plural(CustomerType type) => type switch
    {
        CustomerType.Customer => "Customers",
        CustomerType.Prospect => "Prospects",
        CustomerType.ExpandedProspect => "Expanded Prospects",
        _ => type.ToString()
    };

    public static string Controller(CustomerType type) => type switch
    {
        CustomerType.Customer => "Customers",
        CustomerType.Prospect => "Prospects",
        CustomerType.ExpandedProspect => "ExpandedProspects",
        _ => "Customers"
    };

    public static bool IsProspectLike(CustomerType type) =>
        type is CustomerType.Prospect or CustomerType.ExpandedProspect;

    public static string BadgeClass(CustomerType type) => type switch
    {
        CustomerType.Prospect => "bg-info",
        CustomerType.ExpandedProspect => "bg-warning text-dark",
        _ => "bg-primary"
    };
}
