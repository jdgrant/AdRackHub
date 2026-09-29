using AdRackHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdRackHub.Controllers;

[Authorize(Policy = AppRoles.Customers)]
public class ExpandedProspectsController : Controller
{
    public IActionResult Index(string? search, CustomerStatus? status, int? routeId) =>
        RedirectToAction("Index", "Prospects", new { search, status });

    public IActionResult Create(int? routeId) =>
        RedirectToAction("Create", "Customers", new { type = CustomerType.Prospect });
}
