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
    private readonly WaveApiService _waveApi;
    private readonly WavePocTokenStore _wavePocTokens;
    private readonly WaveSessionService _waveSession;
    private readonly WaveInvoiceWorkflowService _waveInvoices;
    private readonly InvoicePdfStorageService _invoicePdfs;
    private readonly BrochureOptimizeService _brochureOptimize;

    private const string WavePocTokenKey = "WavePocAccessToken";
    private const string WavePocBusinessIdKey = "WavePocBusinessId";
    private const string WavePocBusinessNameKey = "WavePocBusinessName";
    private const string WavePocStateKey = "WavePocOAuthState";
    private const string WavePocClientIdKey = "WavePocClientId";
    private const string WavePocClientSecretKey = "WavePocClientSecret";

    public AdminController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IOptions<WaveSyncOptions> waveSyncOptions,
        IOptions<WaveOptions> waveOptions,
        WaveApiService waveApi,
        WavePocTokenStore wavePocTokens,
        WaveSessionService waveSession,
        WaveInvoiceWorkflowService waveInvoices,
        InvoicePdfStorageService invoicePdfs,
        BrochureOptimizeService brochureOptimize)
    {
        _context = context;
        _userManager = userManager;
        _waveSyncOptions = waveSyncOptions.Value;
        _waveOptions = waveOptions.Value;
        _waveApi = waveApi;
        _wavePocTokens = wavePocTokens;
        _waveSession = waveSession;
        _waveInvoices = waveInvoices;
        _invoicePdfs = invoicePdfs;
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
        var baseUrl = PublicBaseUrl();
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

    public async Task<IActionResult> WaveProof(int? contractId, CancellationToken cancellationToken)
    {
        var model = await BuildWaveProofModelAsync(contractId, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult WaveOAuthSaveCredentials(string? clientId, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            TempData["Error"] = "Paste both the Wave Client ID and Client Secret, then save.";
            return RedirectToAction(nameof(WaveProof));
        }

        HttpContext.Session.SetString(WavePocClientIdKey, clientId.Trim());
        HttpContext.Session.SetString(WavePocClientSecretKey, clientSecret.Trim());
        TempData["Message"] = "Wave app credentials saved for this browser session. Click Connect to Wave.";
        return RedirectToAction(nameof(WaveProof));
    }

    public IActionResult WaveOAuthStart()
    {
        var (clientId, clientSecret) = WavePocAppCredentials();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            TempData["Error"] = "Paste the Wave Client ID and Client Secret below, then Connect to Wave.";
            return RedirectToAction(nameof(WaveProof));
        }

        var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        HttpContext.Session.SetString(WavePocStateKey, state);
        return Redirect(_waveApi.BuildAuthorizationUrl(WaveOAuthRedirectUri(), state, clientId: clientId));
    }

    public async Task<IActionResult> WaveOAuthCallback(string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            TempData["Error"] = $"Wave authorization failed: {error}";
            return RedirectToAction(nameof(WaveProof));
        }

        var expectedState = HttpContext.Session.GetString(WavePocStateKey);
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) || state != expectedState)
        {
            TempData["Error"] = "Wave authorization state did not match. Try Connect to Wave again.";
            return RedirectToAction(nameof(WaveProof));
        }

        var (clientId, clientSecret) = WavePocAppCredentials();
        var tokenResult = await _waveApi.ExchangeAuthorizationCodeAsync(
            code,
            WaveOAuthRedirectUri(),
            cancellationToken,
            clientId,
            clientSecret);
        if (!tokenResult.Success || string.IsNullOrWhiteSpace(tokenResult.AccessToken))
        {
            TempData["Error"] = tokenResult.ErrorMessage ?? "Wave token exchange failed.";
            return RedirectToAction(nameof(WaveProof));
        }

        HttpContext.Session.SetString(WavePocTokenKey, tokenResult.AccessToken);
        var businesses = await _waveApi.ListBusinessesAsync(tokenResult.AccessToken, cancellationToken);
        var business = businesses.FirstOrDefault(b => b.Id == _waveOptions.BusinessId)
            ?? businesses.FirstOrDefault(b => b.Id == tokenResult.BusinessId)
            ?? businesses.FirstOrDefault();
        if (business != null)
        {
            HttpContext.Session.SetString(WavePocBusinessIdKey, business.Id);
            HttpContext.Session.SetString(WavePocBusinessNameKey, business.Name);
        }

        var existing = _wavePocTokens.Load();
        var expiresIn = tokenResult.ExpiresIn > 0 ? tokenResult.ExpiresIn : 90 * 60;
        _wavePocTokens.Save(new WavePocStoredConnection
        {
            AccessToken = tokenResult.AccessToken,
            RefreshToken = tokenResult.RefreshToken ?? existing?.RefreshToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            BusinessId = business?.Id ?? tokenResult.BusinessId,
            BusinessName = business?.Name,
            RedirectUri = WaveOAuthRedirectUri()
        });

        TempData["Message"] = business == null
            ? "Connected to Wave. No businesses were returned for this login."
            : $"Connected to Wave business {business.Name}.";
        return RedirectToAction(nameof(WaveProof));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult WaveOAuthDisconnect()
    {
        ClearWaveLogin();
        TempData["Message"] = "Disconnected the Wave proof-of-concept login.";
        return RedirectToAction(nameof(WaveProof));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult WaveOAuthResetBusiness()
    {
        ClearWaveLogin();
        TempData["Message"] = "Wave business connection was reset. Connect to Wave, then send the contract again.";
        if (WavePocOAuthConfigured())
            return RedirectToAction(nameof(WaveOAuthStart));
        return RedirectToAction(nameof(WaveProof));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WaveProofRun(int contractId, CancellationToken cancellationToken)
    {
        var model = await BuildWaveProofModelAsync(contractId, cancellationToken);
        var (accessToken, businessId) = await EnsureWavePocCredentialsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(businessId))
        {
            if (_waveSession.NeedsBusinessReset)
            {
                TempData["WaveSessionExpired"] = true;
                return RedirectToAction(nameof(WaveProof), new { contractId });
            }

            model.ResultMessage = "Connect to Wave (or set Wave:AccessToken and Wave:BusinessId) before creating a draft invoice.";
            return View("WaveProof", model);
        }

        var contract = await LoadContractForWaveProofAsync(contractId, cancellationToken);
        if (contract == null)
        {
            model.ResultMessage = $"Contract {contractId} was not found.";
            return View("WaveProof", model);
        }

        var memo = $"AdRackHub Wave proof of concept — contract #{contract.Id} {contract.ContractName}";
        var result = await _waveInvoices.ProcessContractAsync(contract, contract.NextBillDate, memo, cancellationToken);
        if (result.SessionExpired)
        {
            TempData["WaveSessionExpired"] = true;
            return RedirectToAction(nameof(WaveProof), new { contractId });
        }
        model.ExistingWaveCustomerId = result.WaveCustomerId ?? contract.Customer.WaveCustomerId;
        if (result.Invoice != null)
            ApplyWaveInvoiceToModel(model, result.Invoice);
        model.HasSavedInvoicePdf = result.PdfSaved || _invoicePdfs.ResolveFilePath(contract.WaveInvoicePdfPath, contract.WaveInvoiceNumber, contract.Id) != null;
        model.Success = result.Success;
        model.InvoiceApproved = result.InvoiceApproved;
        model.CanApproveInvoice = !result.InvoiceApproved && !string.IsNullOrWhiteSpace(model.WaveInvoiceId);
        model.CanCreateInvoice = result.Success || string.IsNullOrWhiteSpace(model.WaveInvoiceId);
        model.ResultMessage = result.Message;
        return View("WaveProof", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WaveProofApprove(
        int contractId,
        string waveInvoiceId,
        string? waveInvoiceNumber,
        string? waveInvoiceUrl,
        CancellationToken cancellationToken)
    {
        var model = await BuildWaveProofModelAsync(contractId, cancellationToken);
        model.WaveInvoiceId = waveInvoiceId;
        model.WaveInvoiceNumber = waveInvoiceNumber;
        model.WaveInvoiceUrl = waveInvoiceUrl;

        var contract = await LoadContractForWaveProofAsync(contractId, cancellationToken);
        if (contract == null)
        {
            model.ResultMessage = $"Contract {contractId} was not found.";
            model.CanApproveInvoice = !string.IsNullOrWhiteSpace(waveInvoiceId);
            return View("WaveProof", model);
        }

        var result = await _waveInvoices.ApprovePersistAndSendAsync(contract, waveInvoiceId, cancellationToken);
        if (result.SessionExpired)
        {
            TempData["WaveSessionExpired"] = true;
            return RedirectToAction(nameof(WaveProof), new { contractId });
        }
        if (result.Invoice != null)
            ApplyWaveInvoiceToModel(model, result.Invoice);
        model.WaveInvoiceId = result.Invoice?.WaveInvoiceId ?? waveInvoiceId;
        model.WaveInvoiceNumber = result.Invoice?.InvoiceNumber ?? waveInvoiceNumber;
        model.WaveInvoiceUrl = result.Invoice?.WaveInvoiceUrl ?? waveInvoiceUrl;
        model.HasSavedInvoicePdf = result.PdfSaved || _invoicePdfs.ResolveFilePath(contract.WaveInvoicePdfPath, contract.WaveInvoiceNumber, contract.Id) != null;
        model.Success = result.Success;
        model.InvoiceApproved = result.InvoiceApproved;
        model.CanApproveInvoice = !result.InvoiceApproved && !string.IsNullOrWhiteSpace(model.WaveInvoiceId);
        model.ResultMessage = result.Message;
        return View("WaveProof", model);
    }

    [HttpGet]
    public async Task<IActionResult> WaveProofPdf(string invoiceId, string? invoiceNumber, CancellationToken cancellationToken)
    {
        var (accessToken, businessId) = await EnsureWavePocCredentialsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(businessId))
            return BadRequest("Connect to Wave before downloading an invoice PDF.");
        if (string.IsNullOrWhiteSpace(invoiceId))
            return BadRequest("A Wave invoice id is required.");

        var bytes = await _waveApi.DownloadInvoicePdfAsync(invoiceId, cancellationToken, accessToken, businessId);
        if (bytes == null || bytes.Length == 0)
            return NotFound("Wave did not return a PDF for this invoice yet. Open it in Wave or try again in a moment.");

        return File(bytes, "application/pdf", InvoicePdfStorageService.BuildFileName(invoiceNumber));
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

    public IActionResult ProspectHotels() =>
        RedirectToAction("Index", "ProspectStops");

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

    private async Task<WaveProofViewModel> BuildWaveProofModelAsync(int? contractId, CancellationToken cancellationToken)
    {
        var (accessToken, _) = await EnsureWavePocCredentialsAsync(cancellationToken);
        var stored = _wavePocTokens.Load();
        var model = new WaveProofViewModel
        {
            RedirectUri = WaveOAuthRedirectUri(),
            OAuthConfigured = WavePocOAuthConfigured(),
            ClientId = WavePocAppCredentials().ClientId,
            Connected = !string.IsNullOrWhiteSpace(accessToken),
            NeedsBusinessReset = _waveSession.NeedsBusinessReset,
            BusinessName = stored?.BusinessName ?? HttpContext.Session.GetString(WavePocBusinessNameKey),
            ContractId = contractId
        };

        if (contractId is not > 0)
            return model;

        var contract = await LoadContractForWaveProofAsync(contractId.Value, cancellationToken);
        if (contract == null)
        {
            model.ResultMessage = $"Contract {contractId} was not found.";
            return model;
        }

        model.CustomerName = contract.Customer.CustomerName;
        model.ContractName = contract.ContractName;
        model.ExistingWaveCustomerId = contract.Customer.WaveCustomerId;
        model.WaveInvoiceNumber = contract.WaveInvoiceNumber;
        model.WaveInvoiceId = contract.WaveInvoiceId;
        model.HasSavedInvoicePdf = _invoicePdfs.ResolveFilePath(contract.WaveInvoicePdfPath, contract.WaveInvoiceNumber, contract.Id) != null;
        model.InvoiceReceiptMethod = contract.Customer.InvoiceReceiptMethod;
        model.WillEmailInvoice = contract.Customer.InvoiceReceiptMethod.IncludesEmail();
        model.InvoiceRecipients = model.WillEmailInvoice
            ? BillingContactHelper.ContactsWithEmail(contract.Customer.Contacts)
                .Select(c => $"{c.DisplayName} <{c.Email}>")
                .ToList()
            : new List<string>();
        model.InvoiceDate = contract.NextBillDate;
        model.DueDate = contract.NextBillDate.AddDays(WaveApiService.InvoiceDueDays);
        var months = AnnualBillingHelper.BillingMonths(contract);
        var missingRoutes = new List<string>();
        model.LineSummaries = contract.ContractRoutes
            .OrderBy(cr => cr.Route.RouteName)
            .Select(cr =>
            {
                var amount = AnnualBillingHelper.GetBillingAmount(cr);
                var productId = cr.Route.WaveProductId;
                if (string.IsNullOrWhiteSpace(productId))
                    missingRoutes.Add(cr.Route.RouteName);
                var productLabel = string.IsNullOrWhiteSpace(productId)
                    ? "missing Wave Product ID"
                    : $"Wave product {productId}";
                return $"{cr.Route.RouteName} — {amount:C} ({months} month{(months == 1 ? "" : "s")}) — {productLabel}";
            })
            .ToList();
        model.CanCreateInvoice = missingRoutes.Count == 0 && model.LineSummaries.Count > 0;
        if (missingRoutes.Count > 0)
            model.ResultMessage = "Set Wave Product ID on these routes first: " + string.Join(", ", missingRoutes);
        return model;
    }

    private Task<CustomerContract?> LoadContractForWaveProofAsync(int contractId, CancellationToken cancellationToken) =>
        _context.CustomerContracts
            .AsSplitQuery()
            .Include(c => c.Customer)
                .ThenInclude(c => c.Contacts)
            .Include(c => c.Customer)
                .ThenInclude(c => c.CustomerRoutes)
                    .ThenInclude(cr => cr.CustomerRouteStops)
                        .ThenInclude(crs => crs.Stop)
            .Include(c => c.ContractRoutes)
                .ThenInclude(cr => cr.Route)
                    .ThenInclude(r => r.Stops)
            .FirstOrDefaultAsync(c => c.Id == contractId, cancellationToken);

    private static void ApplyWaveInvoiceToModel(WaveProofViewModel model, WaveInvoiceResult invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.WaveInvoiceId))
            model.WaveInvoiceId = invoice.WaveInvoiceId;
        if (!string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
            model.WaveInvoiceNumber = invoice.InvoiceNumber;
        if (!string.IsNullOrWhiteSpace(invoice.WaveInvoiceUrl))
            model.WaveInvoiceUrl = invoice.WaveInvoiceUrl;
        if (!string.IsNullOrWhiteSpace(invoice.PdfUrl))
            model.WaveInvoicePdfUrl = invoice.PdfUrl;
        if (!string.IsNullOrWhiteSpace(invoice.DueDate) && DateOnly.TryParse(invoice.DueDate, out var dueDate))
            model.DueDate = dueDate;
        if (!string.IsNullOrWhiteSpace(invoice.DueDate))
            model.WaveInvoiceDueDate = invoice.DueDate;
    }

    private void ClearWaveLogin()
    {
        HttpContext.Session.Remove(WavePocTokenKey);
        HttpContext.Session.Remove(WavePocBusinessIdKey);
        HttpContext.Session.Remove(WavePocBusinessNameKey);
        HttpContext.Session.Remove(WavePocStateKey);
        _wavePocTokens.Clear();
    }

    private async Task<(string? AccessToken, string? BusinessId)> EnsureWavePocCredentialsAsync(CancellationToken cancellationToken)
    {
        var (clientId, clientSecret) = WavePocAppCredentials();
        var creds = await _waveSession.GetCredentialsAsync(
            cancellationToken,
            clientId,
            clientSecret,
            WaveOAuthRedirectUri());
        if (creds != null)
        {
            HttpContext.Session.SetString(WavePocTokenKey, creds.AccessToken);
            if (!string.IsNullOrWhiteSpace(creds.BusinessId))
                HttpContext.Session.SetString(WavePocBusinessIdKey, creds.BusinessId);
            if (!string.IsNullOrWhiteSpace(creds.BusinessName))
                HttpContext.Session.SetString(WavePocBusinessNameKey, creds.BusinessName);
            return (creds.AccessToken, creds.BusinessId);
        }

        if (_waveSession.NeedsBusinessReset)
            return (null, null);

        return WavePocCredentials();
    }

    private (string? AccessToken, string? BusinessId) WavePocCredentials()
    {
        var sessionToken = HttpContext.Session.GetString(WavePocTokenKey);
        var sessionBusiness = HttpContext.Session.GetString(WavePocBusinessIdKey);
        return (
            string.IsNullOrWhiteSpace(sessionToken) ? _waveOptions.AccessToken : sessionToken,
            string.IsNullOrWhiteSpace(sessionBusiness) ? _waveOptions.BusinessId : sessionBusiness);
    }

    private (string? ClientId, string? ClientSecret) WavePocAppCredentials()
    {
        var sessionClientId = HttpContext.Session.GetString(WavePocClientIdKey);
        var sessionClientSecret = HttpContext.Session.GetString(WavePocClientSecretKey);
        return (
            string.IsNullOrWhiteSpace(sessionClientId) ? _waveOptions.ClientId : sessionClientId,
            string.IsNullOrWhiteSpace(sessionClientSecret) ? _waveOptions.ClientSecret : sessionClientSecret);
    }

    private bool WavePocOAuthConfigured()
    {
        var (clientId, clientSecret) = WavePocAppCredentials();
        return !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);
    }

    private string WaveOAuthRedirectUri() => $"{PublicBaseUrl()}/Admin/WaveOAuthCallback";

    private string PublicBaseUrl()
    {
        var scheme = Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? Request.Scheme;
        if (scheme.Contains(',', StringComparison.Ordinal))
            scheme = scheme.Split(',')[0].Trim();
        return $"{scheme}://{Request.Host}";
    }
}
