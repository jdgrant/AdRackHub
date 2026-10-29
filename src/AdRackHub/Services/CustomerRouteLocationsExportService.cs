using AdRackHub.Models;
using ClosedXML.Excel;

namespace AdRackHub.Services;

public static class CustomerRouteLocationsExportService
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string WorksheetName = "Route locations";

    private static readonly string[] Headers =
    {
        "Route",
        "#",
        "Stop",
        "Type",
        "Location",
        "Highway / Exit"
    };

    public static string FileName(Customer customer)
    {
        var name = string.IsNullOrWhiteSpace(customer.CustomerName)
            ? $"customer-{customer.Id}"
            : customer.CustomerName.Trim();
        var invalid = Path.GetInvalidFileNameChars()
            .Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|'])
            .ToHashSet();
        var safe = new string(name.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safe))
            safe = $"customer-{customer.Id}";
        return $"{safe} route locations.xlsx";
    }

    public static void Write(Stream outputStream, IReadOnlyList<ContractedRouteOutline> outlines)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(WorksheetName);

        for (var column = 0; column < Headers.Length; column++)
            sheet.Cell(1, column + 1).Value = Headers[column];

        var header = sheet.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;

        var row = 2;
        foreach (var outline in outlines)
        {
            foreach (var stop in outline.Stops)
            {
                sheet.Cell(row, 1).Value = outline.Route.RouteName;
                if (stop.StepNumber.HasValue)
                    sheet.Cell(row, 2).Value = stop.StepNumber.Value;
                else
                    sheet.Cell(row, 2).Value = "—";
                sheet.Cell(row, 3).Value = stop.StopName;
                sheet.Cell(row, 4).Value = stop.StopType.ToString();
                sheet.Cell(row, 5).Value = CustomerRouteLocations.FormatLocation(stop);
                sheet.Cell(row, 6).Value = string.IsNullOrWhiteSpace(stop.HighwayExit)
                    ? "—"
                    : stop.HighwayExit.Trim();
                row++;
            }
        }

        sheet.SheetView.FreezeRows(1);
        var lastRow = Math.Max(row - 1, 1);
        sheet.Range(1, 1, lastRow, Headers.Length).SetAutoFilter();
        sheet.Column(2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        sheet.Column(1).Width = 36;
        sheet.Column(2).Width = 8;
        sheet.Column(3).Width = 36;
        sheet.Column(4).Width = 14;
        sheet.Column(5).Width = 48;
        sheet.Column(6).Width = 24;

        workbook.SaveAs(outputStream);
    }
}
