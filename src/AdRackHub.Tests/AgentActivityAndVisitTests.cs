using System.Text;
using AdRackHub.Data;
using AdRackHub.Models;
using AdRackHub.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdRackHub.Tests;

public class AgentActivityAndVisitTests
{
    [Fact]
    public void EmailPhotoMustBeAnImage()
    {
        var jpeg = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        var image = VisitImageRules.FromBase64("data:image/jpeg;base64," + jpeg, "before.jpg", "Before photo");
        Assert.Equal("image/jpeg", image.ContentType);
        Assert.Equal("before.jpg", image.OriginalFileName);

        var text = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello from the email"));
        var error = Assert.Throws<InvalidOperationException>(() => VisitImageRules.FromBase64(text, "note.txt", "Before photo"));
        Assert.Contains("JPG", error.Message);
    }

    [Fact]
    public async Task OpenActivitiesIncludeTasksAndEmailFollowUps()
    {
        await using var db = NewDb();
        db.Customers.Add(new Customer { CustomerName = "Riverside Museum", Type = CustomerType.Customer, Status = CustomerStatus.Active });
        await db.SaveChangesAsync();
        var customerId = db.Customers.Single().Id;
        db.CustomerNotes.AddRange(
            new CustomerNote { CustomerId = customerId, Kind = CustomerNoteKind.Task, Status = CustomerNoteStatus.New, Body = "Call the buyer", DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)), CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new CustomerNote { CustomerId = customerId, Kind = CustomerNoteKind.Email, Status = CustomerNoteStatus.Working, Body = "Waiting on the brochure reply", CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new CustomerNote { CustomerId = customerId, Kind = CustomerNoteKind.Email, Status = CustomerNoteStatus.Done, Body = "Sent the rate sheet", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var api = new AgentActivityService(db);
        var open = await api.ListAsync(null, null, null, null, 1, 50, "https://hub.ad-rack.net", CancellationToken.None);
        Assert.Equal(2, open.Body!.Total);
        Assert.Contains(open.Body.Items, item => item.Kind == "Task" && item.IsOverdue);
        Assert.Contains(open.Body.Items, item => item.Kind == "Email" && item.Status == "Working");
        Assert.DoesNotContain(open.Body.Items, item => item.Status == "Done");

        var tasks = await api.ListAsync("open", "Task", customerId, null, 1, 50, "https://hub.ad-rack.net", CancellationToken.None);
        Assert.Equal("Call the buyer", Assert.Single(tasks.Body!.Items).Body);
        Assert.Contains("/Customers/Details/", tasks.Body.Items[0].HubUrl);
    }

    [Fact]
    public async Task VisitUploadFromEmailSavesDriverAndBothPhotos()
    {
        var root = Path.Combine(Path.GetTempPath(), "adrack-visits-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var db = NewDb();
            var route = new Route { RouteName = "I-75 Exits", Price = 10m, Status = RouteStatus.Active };
            db.Routes.Add(route);
            await db.SaveChangesAsync();
            db.Stops.Add(new Stop { RouteId = route.Id, StopName = "Pilot Travel Center", StopType = StopType.Hotel, Status = StopStatus.Active });
            await db.SaveChangesAsync();

            var photos = new VisitPhotoStore(root);
            var api = new AgentVisitService(db, new StopVisitService(db, photos));
            var jpeg = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
            var saved = await api.LogAsync(new AgentVisitUpload
            {
                StopName = "Pilot",
                RouteName = "I-75",
                DriverName = "Sam Rivera",
                BeforeFileName = "before.jpg",
                BeforeImageBase64 = jpeg,
                AfterFileName = "after.jpg",
                AfterImageBase64 = jpeg,
                Notes = "Restocked"
            }, "https://hub.ad-rack.net", null, "agent@ad-rack.net", CancellationToken.None);

            Assert.Equal(201, saved.StatusCode);
            Assert.Equal("Sam Rivera", saved.Body!.DriverName);
            Assert.Equal(2, saved.Body.Photos.Count);
            Assert.Contains(saved.Body.Photos, photo => photo.Kind == "Before");
            Assert.Contains(saved.Body.Photos, photo => photo.Kind == "After");
            Assert.Equal(2, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);

            var stop = await db.Stops.SingleAsync();
            Assert.NotNull(stop.LastVisitedAt);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AmbiguousStopDoesNotSaveAVisit()
    {
        await using var db = NewDb();
        db.Routes.AddRange(
            new Route { RouteName = "I-75 Exits", Price = 10m, Status = RouteStatus.Active },
            new Route { RouteName = "I-64 Exits", Price = 10m, Status = RouteStatus.Active });
        await db.SaveChangesAsync();
        var routes = await db.Routes.OrderBy(r => r.Id).ToListAsync();
        db.Stops.AddRange(
            new Stop { RouteId = routes[0].Id, StopName = "Pilot Travel Center", StopType = StopType.Hotel, Status = StopStatus.Active },
            new Stop { RouteId = routes[1].Id, StopName = "Pilot Travel Center", StopType = StopType.Hotel, Status = StopStatus.Active });
        await db.SaveChangesAsync();

        var api = new AgentVisitService(db, new StopVisitService(db, new VisitPhotoStore(Path.GetTempPath())));
        var jpeg = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        var blocked = await api.LogAsync(new AgentVisitUpload
        {
            StopName = "Pilot",
            DriverName = "Sam Rivera",
            BeforeImageBase64 = jpeg,
            AfterImageBase64 = jpeg
        }, "https://hub.ad-rack.net", null, "Agent API", CancellationToken.None);

        Assert.Equal(409, blocked.StatusCode);
        Assert.Contains("stopId", blocked.Error!.Error);
        Assert.Equal(0, await db.StopVisits.CountAsync());
    }

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
