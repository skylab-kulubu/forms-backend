namespace Skylab.Exports.Application.Contracts;

public record ExcelExportRequest(
    string? SheetName,
    List<string> Headers,
    List<List<string>> Rows
);