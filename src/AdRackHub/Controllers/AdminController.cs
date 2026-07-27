using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using AdRackHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AdRackHub.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly WaveSyncOptions _waveSyncOptions;
    private readonly WaveOptions _waveOptions;
    private readonly BrochureOptimizeService _brochureOptimize;

    public AdminController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IOptions<WaveSyncOptions> waveSyncOptions,
        IOptions<WaveOptions> waveOptions,
        BrochureOptimizeService brochureOptimize)
    {
        _context = context;
        _userManager = userManager;
        _waveSyncOptions = waveSyncOptions.Value;
        _waveOptions = waveOptions.Value;
        _brochureOptimize = brochureOptimize;
    }

    public async Task<IActionResult> Index()
    {
        var users = await _userManager.Users.OrderBy(u => u.Email).ToListAsync();
        var rows = new List<AdminUserRow>();
        foreach (var user in users)
        {
            rows.Add(new AdminUserRow
            {
                Id = user.Id,
                Email = user.Email ?? user.UserName ?? string.Empty,
                DisplayName = user.DisplayName,
                Roles = await _userManager.GetRolesAsync(user),
                IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow
            });
        }

        var model = new AdminIndexViewModel
        {
            UserCount = users.Count,
            CustomerCount = await _context.Customers.CountAsync(),
            RouteCount = await _context.Routes.CountAsync(),
            StopCount = await _context.Stops.CountAsync(),
            Users = rows
        };

        return View(model);
    }

    public IActionResult WaveSync()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var model = new WaveSyncSetupViewModel
        {
            BaseUrl = baseUrl,
            InboundConfigured = !string.IsNullOrWhiteSpace(_waveSyncOptions.InboundWebhookSecret),
            OutboundConfigured = !string.IsNullOrWhiteSpace(_waveSyncOptions.OutboundWebhookUrl),
            WaveApiConfigured = _waveOptions.IsConfigured,
            PushCustomersToWaveApi = _waveSyncOptions.PushCustomersToWaveApi
        };
        return View(model);
    }

    public async Task<IActionResult> OptimizeBrochures(CancellationToken cancellationToken)
    {
        var model = new OptimizeBrochuresViewModel
        {
            Stats = await _brochureOptimize.GetStatsAsync(cancellationToken)
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OptimizeBrochuresConfirm(CancellationToken cancellationToken)
    {
        var result = await _brochureOptimize.OptimizeAllAsync(cancellationToken);
        TempData["Message"] = result.Optimized > 0
            ? $"Optimized {result.Optimized} brochure(s). Saved {FormatBytes(result.BytesSaved)}."
            : "No brochures needed optimization.";
        if (result.Failed > 0)
            TempData["Error"] = $"{result.Failed} file(s) failed. See details below.";

        var model = new OptimizeBrochuresViewModel
        {
            Stats = await _brochureOptimize.GetStatsAsync(cancellationToken),
            LastResult = result
        };
        return View("OptimizeBrochures", model);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }

    public IActionResult CreateUser() =>
        View(new CreateAdminUserViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(CreateAdminUserViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true,
            DisplayName = model.DisplayName
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await SyncRolesAsync(user, model.SelectedRoles);
        TempData["Message"] = $"User {model.Email} created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> EditUser(string? id)
    {
        if (id == null)
            return NotFound();

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound();

        var model = new EditAdminUserViewModel
        {
            Id = user.Id,
            Email = user.Email ?? user.UserName ?? string.Empty,
            DisplayName = user.DisplayName,
            SelectedRoles = (await _userManager.GetRolesAsync(user)).ToList(),
            IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(string id, EditAdminUserViewModel model)
    {
        if (id != model.Id)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(model.NewPassword) || !string.IsNullOrWhiteSpace(model.ConfirmNewPassword))
        {
            if (string.IsNullOrWhiteSpace(model.NewPassword))
                ModelState.AddModelError(nameof(model.NewPassword), "Enter a new password or leave both fields blank.");
        }

        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound();

        user.DisplayName = model.DisplayName;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        if (!string.IsNullOrWhiteSpace(model.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var passwordResult = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!passwordResult.Succeeded)
            {
                foreach (var error in passwordResult.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);
                return View(model);
            }
        }

        await SyncRolesAsync(user, model.SelectedRoles);

        if (model.IsLockedOut)
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        else
            await _userManager.SetLockoutEndDateAsync(user, null);

        TempData["Message"] = $"User {model.Email} updated.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> DeleteUser(string? id)
    {
        if (id == null)
            return NotFound();

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound();

        if (user.Id == _userManager.GetUserId(User))
            return RedirectToAction(nameof(Index));

        return View(user);
    }

    [HttpPost, ActionName("DeleteUser")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUserConfirmed(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return RedirectToAction(nameof(Index));

        if (user.Id == _userManager.GetUserId(User))
        {
            TempData["Error"] = "You cannot delete your own account.";
            return RedirectToAction(nameof(Index));
        }

        var email = user.Email;
        await _userManager.DeleteAsync(user);
        TempData["Message"] = $"User {email} deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task SyncRolesAsync(ApplicationUser user, IEnumerable<string> selectedRoles)
    {
        var validRoles = selectedRoles
            .Where(r => AppRoles.All.Contains(r))
            .Distinct()
            .ToList();

        var currentRoles = await _userManager.GetRolesAsync(user);
        var toRemove = currentRoles.Except(validRoles).ToList();
        var toAdd = validRoles.Except(currentRoles).ToList();

        if (toRemove.Count > 0)
            await _userManager.RemoveFromRolesAsync(user, toRemove);
        if (toAdd.Count > 0)
            await _userManager.AddToRolesAsync(user, toAdd);
    }
}
