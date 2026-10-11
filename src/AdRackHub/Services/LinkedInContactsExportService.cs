using AdRackHub.ViewModels;
using ClosedXML.Excel;

namespace AdRackHub.Services;

public static class LinkedInContactsExportService
{
    public const string WorksheetName = "Contacts";
    public const string ProfileUrlColumn = "LinkedIn Profile URL";

    public static string FileName(DateTime asOf) => $"linkedin-contacts-{asOf:yyyyMMdd}.xlsx";

    public static void Write(Stream outputStream, IReadOnlyList<LinkedInContactRow> rows)
    {
        using var workbook = new XLWorkbook();
        WriteInstructions(workbook);
        var sheet = workbook.Worksheets.Add(WorksheetName);

        string[] headers =
        {
            "ContactId",
            "First name",
            "Last name",
            "Person",
            "Title",
            "Customer",
            "Type",
            "Role",
            "Email",
            "Phone",
            "People search URL",
            "Company search URL",
            ProfileUrlColumn
        };

        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(1, i + 1).Value = headers[i];

        var header = sheet.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;

        var row = 2;
        foreach (var contact in rows)
        {
            sheet.Cell(row, 1).Value = contact.ContactId;
            sheet.Cell(row, 2).Value = contact.FirstName ?? "";
            sheet.Cell(row, 3).Value = contact.LastName ?? "";
            sheet.Cell(row, 4).Value = contact.PersonName ?? "";
            sheet.Cell(row, 5).Value = contact.Title ?? "";
            sheet.Cell(row, 6).Value = contact.CustomerName;
            sheet.Cell(row, 7).Value = contact.CustomerType == Models.CustomerType.Customer ? "Customer" : "Prospect";
            sheet.Cell(row, 8).Value = contact.Role.ToString();
            sheet.Cell(row, 9).Value = contact.Email ?? "";
            sheet.Cell(row, 10).Value = contact.Phone ?? "";
            SetHyperlink(sheet.Cell(row, 11), contact.PeopleSearchUrl);
            SetHyperlink(sheet.Cell(row, 12), contact.CompanySearchUrl);
            if (contact.HasProfile)
                SetHyperlink(sheet.Cell(row, 13), contact.ProfileUrl);
            else
                sheet.Cell(row, 13).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
            row++;
        }

        sheet.SheetView.FreezeRows(1);
        if (row > 2)
            sheet.Range(1, 1, row - 1, headers.Length).SetAutoFilter();

        sheet.Column(1).Width = 12;
        sheet.Column(2).Width = 16;
        sheet.Column(3).Width = 18;
        sheet.Column(4).Width = 24;
        sheet.Column(5).Width = 28;
        sheet.Column(6).Width = 36;
        sheet.Column(7).Width = 12;
        sheet.Column(8).Width = 14;
        sheet.Column(9).Width = 32;
        sheet.Column(10).Width = 16;
        sheet.Column(11).Width = 48;
        sheet.Column(12).Width = 48;
        sheet.Column(13).Width = 48;

        workbook.SaveAs(outputStream);
    }

    private static void WriteInstructions(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("How to use");
        sheet.Cell(1, 1).Value = "Finding LinkedIn profiles";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;

        string[] lines =
        {
            "",
            "AdRackHub cannot scrape LinkedIn. This sheet lists your named contacts and opens LinkedIn search.",
            "",
            "1. Stay signed in to LinkedIn in your browser.",
            "2. On the Contacts sheet, click a People search URL.",
            "3. Pick the matching person, open their profile, and copy the URL from the address bar.",
            "4. Paste that URL into LinkedIn Profile URL (the yellow column). Do not paste the search results URL.",
            "5. Company search URL is for the business page when you need it.",
            "",
            "ContactId is the AdRackHub contact id so these rows can be imported later."
        };

        for (var i = 0; i < lines.Length; i++)
            sheet.Cell(i + 2, 1).Value = lines[i];

        sheet.Column(1).Width = 110;
        sheet.Style.Alignment.WrapText = true;
    }

    private static void SetHyperlink(IXLCell cell, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        cell.Value = url;
        cell.SetHyperlink(new XLHyperlink(url));
        cell.Style.Font.FontColor = XLColor.FromHtml("#0563C1");
        cell.Style.Font.Underline = XLFontUnderlineValues.Single;
    }
}
