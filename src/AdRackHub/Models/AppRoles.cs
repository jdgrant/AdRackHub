namespace AdRackHub.Models;

public static class AppRoles
{
    public const string Dashboard = "Dashboard";
    public const string Customers = "Customers";
    public const string RoutesStops = "RoutesStops";
    public const string Admin = "Admin";

    public static readonly string[] All =
    [
        Dashboard,
        Customers,
        RoutesStops,
        Admin
    ];
}
