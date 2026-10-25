using System.Net;
using ClosedXML.Excel;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ResponseExportTests(FormsApiFactory api)
{
    [Fact]
    public async Task A_title_with_characters_excel_forbids_still_exports_in_club_time_and_turkish()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var form = await owner.CreateFormAsync(Api.NewForm("2026/27 Başvuru: Ekim [ön]", Api.Question("name")) with { RequiresManualReview = true });
        await api.ClientFor(api.Users.Add("Mehmet Kaya").Id).SubmitAsync(form.Id, ("name", "Mehmet"));
        var submittedAt = Assert.Single((await (await owner.GetAsync($"/api/admin/forms/{form.Id}/responses")).DataAsync<FormResponsesListResult>()).PaginationData.Items).SubmittedAt!.Value;

        var export = await owner.GetAsync($"/api/admin/forms/{form.Id}/responses/export");

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);

        using var workbook = new XLWorkbook(await export.Content.ReadAsStreamAsync());
        var sheet = Assert.Single(workbook.Worksheets);
        var istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

        Assert.Equal("2026-27 Başvuru- Ekim -ön-", sheet.Name);
        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(submittedAt, DateTimeKind.Utc), istanbul).ToString("dd.MM.yyyy HH:mm"), sheet.Cell(2, 3).GetString());
        Assert.Equal("Beklemede", sheet.Cell(2, 4).GetString());
    }
}
