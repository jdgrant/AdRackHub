using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdRackHub.Tests;

public class InvoiceReminderTemplateTests
{
    [Fact]
    public void Apply_FillsMergeFieldsAndDropsAnEmptyPayLink()
    {
        var data = InvoiceReminderMerge.Sample with { InvoiceLink = "" };
        var text = InvoiceReminderMerge.Apply(InvoiceReminderMerge.DefaultBody, data);

        Assert.Contains("Sample Customer", text);
        Assert.Contains("45 days", text);
        Assert.Contains("March 2026", text);
        Assert.DoesNotContain("{{", text);
        Assert.DoesNotContain("View and pay this invoice:", text);
    }

    [Fact]
    public async Task Save_RecordsHistory_AndAOneOffRenderDoesNotChangeTheTemplate()
    {
        await using var db = NewDb();
        var service = new InvoiceReminderTemplateService(db);

        var saved = await service.SaveAsync(
            "Reminder {{Invoice number}}",
            "Hello {{Client company name}}",
            "jon@ad-rack.net",
            InvoiceReminderMerge.SavedChange);
        Assert.True(saved.Saved);

        var again = await service.SaveAsync(
            "Reminder {{Invoice number}}",
            "Hello {{Client company name}}",
            "jon@ad-rack.net",
            InvoiceReminderMerge.SavedChange);
        Assert.False(again.Saved);
        Assert.Equal(1, await db.InvoiceReminderTemplateRevisions.CountAsync());

        var rendered = await service.RenderAsync(
            "Only this email {{Client company name}}",
            "Do not save {{Invoice amount}}",
            null);
        Assert.Equal("Only this email Sample Customer", rendered.Subject);
        Assert.Contains("$250.00", rendered.Body);

        var current = await service.GetCurrentAsync();
        Assert.Equal("Reminder {{Invoice number}}", current.Subject);
        Assert.Equal("Hello {{Client company name}}", current.Body);
    }

    [Fact]
    public async Task Restore_WritesTheSystemDefaultAndKeepsThePreviousTextInHistory()
    {
        await using var db = NewDb();
        var service = new InvoiceReminderTemplateService(db);
        await service.SaveAsync("Custom subject", "Custom body", "jon@ad-rack.net", InvoiceReminderMerge.SavedChange);

        var restored = await service.RestoreDefaultAsync("jon@ad-rack.net");
        Assert.True(restored.Saved);

        var current = await service.GetCurrentAsync();
        Assert.Equal(InvoiceReminderMerge.DefaultSubject, current.Subject);
        Assert.Equal(InvoiceReminderMerge.Normalize(InvoiceReminderMerge.DefaultBody), current.Body);

        var history = await service.ListHistoryAsync();
        Assert.Equal(InvoiceReminderMerge.RestoredChange, history[0].Change);
        Assert.Equal("Custom subject", history[1].Subject);
    }

    [Fact]
    public async Task AgentUpdate_IsRejectedUntilTheAdministrativePermissionIsGranted()
    {
        await using var db = NewDb();
        var service = new InvoiceReminderTemplateService(db);
        await service.SaveAsync("Saved subject", "Saved body", "jon@ad-rack.net", InvoiceReminderMerge.SavedChange);

        var denied = await service.UpdateForAgentAsync("Agent subject", "Agent body", false, "https://hub.ad-rack.net");
        Assert.Equal(403, denied.StatusCode);
        Assert.Equal(InvoiceReminderMerge.UpdateDeniedMessage, denied.Error!.Error);
        Assert.Equal("Saved subject", (await service.GetCurrentAsync()).Subject);

        var allowed = await service.UpdateForAgentAsync("Agent subject", "Hello {{Client company name}}", true, "https://hub.ad-rack.net");
        Assert.Equal(200, allowed.StatusCode);
        Assert.True(allowed.Body!.CanUpdate);
        Assert.Equal("Agent subject", allowed.Body.Subject);
        Assert.Equal("Hello Sample Customer", allowed.Body.RenderedBody);
        Assert.Equal(InvoiceReminderMerge.AgentEditor, (await service.ListHistoryAsync())[0].Who);
    }

    [Fact]
    public async Task Render_UsesTheSelectedInvoiceWithoutSaving()
    {
        await using var db = NewDb();
        db.Customers.Add(new Customer { CustomerName = "Riverside Museum", Type = CustomerType.Customer, Status = CustomerStatus.Active });
        await db.SaveChangesAsync();
        var customer = db.Customers.Single();
        db.BillingRuns.Add(new BillingRun
        {
            Year = 2026,
            Month = 3,
            Status = BillingRunStatus.Submitted,
            Invoices =
            [
                new BillingRunInvoice
                {
                    CustomerId = customer.Id,
                    TotalAmount = 80,
                    Status = BillingRunInvoiceStatus.Submitted,
                    WaveInvoiceNumber = "W-100",
                    WaveInvoiceUrl = "https://wave.example/invoices/100"
                }
            ]
        });
        await db.SaveChangesAsync();
        var invoice = db.BillingRunInvoices.Single();

        var service = new InvoiceReminderTemplateService(db);
        var rendered = await service.RenderForAgentAsync(invoice.Id, null, null, "https://hub.ad-rack.net");

        Assert.Equal(200, rendered.StatusCode);
        Assert.False(rendered.Body!.Saved);
        Assert.Contains("Riverside Museum", rendered.Body.Body);
        Assert.Contains("W-100", rendered.Body.Subject);
        Assert.Contains("https://wave.example/invoices/100", rendered.Body.Body);
        Assert.Null((await service.GetCurrentAsync()).UpdatedBy);
    }

    [Fact]
    public void OpenApi_DocumentsReadAndRender_AndTheUpdatePermission()
    {
        var json = AgentApiGuide.OpenApiJson("https://hub.ad-rack.net");
        Assert.Contains("/api/agent/communications/invoice-reminder/render", json);
        Assert.Contains("CanManageInvoiceReminderTemplate", json);
        Assert.DoesNotContain("\"delete\":", json, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
