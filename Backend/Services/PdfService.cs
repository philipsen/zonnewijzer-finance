using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;


namespace Backend.Services
{
    public class PdfService : IPdfService
    {
        public byte[] GenerateInvoicePdf(InvoiceData invoiceData)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "zonnewijzer_logo.png");
            var hasLogo = File.Exists(logoPath);

            var document = Document.Create(container =>
            {
                // ── Page 1: Invoice summary ───────────────────────────────────────
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    page.Header()
                        .PaddingBottom(20)
                        .Column(column =>
                        {
                            if (hasLogo)
                                column.Item().AlignCenter().Height(60).Image(logoPath);

                            column.Item().PaddingTop(10).AlignCenter()
                                .Text("Charging Sessions Invoice")
                                .Bold().FontSize(24).FontColor(Colors.Blue.Medium);
                        });

                    page.Content()
                        .PaddingVertical(1, Unit.Centimetre)
                        .Column(column =>
                        {
                            // Invoice number + period  /  Customer address
                            column.Item().Row(row =>
                            {
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().Text($"Invoice Number: {invoiceData.InvoiceNumber}").Bold();
                                    col.Item().Text($"Period: {invoiceData.MonthName}");
                                    col.Item().PaddingTop(5).Text($"Generated: {invoiceData.GeneratedAt:yyyy-MM-dd HH:mm}");
                                });

                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().AlignRight().Text(invoiceData.UserName).Bold();
                                    if (!string.IsNullOrWhiteSpace(invoiceData.Street))
                                    {
                                        var streetLine = invoiceData.Street;
                                        if (!string.IsNullOrWhiteSpace(invoiceData.HouseNumber))
                                            streetLine += $" {invoiceData.HouseNumber}";
                                        col.Item().AlignRight().Text(streetLine);
                                    }
                                    if (!string.IsNullOrWhiteSpace(invoiceData.PostalCode) || !string.IsNullOrWhiteSpace(invoiceData.PlaceName))
                                    {
                                        var cityLine = $"{invoiceData.PostalCode} {invoiceData.PlaceName}".Trim();
                                        col.Item().AlignRight().Text(cityLine);
                                    }
                                });
                            });

                            column.Item().PaddingTop(20).LineHorizontal(1);

                            // Summary totals
                            column.Item().PaddingTop(30).Row(row =>
                            {
                                row.RelativeItem();
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().Row(r =>
                                    {
                                        r.RelativeItem().Text("Total kWh:").FontSize(14);
                                        r.ConstantItem(120).AlignRight().Text($"{invoiceData.TotalKwh:F2} kWh").FontSize(14);
                                    });
                                    col.Item().PaddingTop(6).Row(r =>
                                    {
                                        r.RelativeItem().Text("Rate per kWh:").FontSize(14);
                                        r.ConstantItem(120).AlignRight().Text($"€{invoiceData.RatePerKwh:F4}").FontSize(14);
                                    });
                                    col.Item().PaddingTop(10).LineHorizontal(1);
                                    col.Item().PaddingTop(8).Row(r =>
                                    {
                                        r.RelativeItem().Text("Total Amount:").Bold().FontSize(18);
                                        r.ConstantItem(120).AlignRight().Text($"€{invoiceData.TotalAmount:F2}").Bold().FontSize(18).FontColor(Colors.Blue.Medium);
                                    });
                                });
                            });

                            // Payment section
                            column.Item().PaddingTop(40).Column(col =>
                            {
                                col.Item().Text("Payment").Bold().FontSize(14);
                                col.Item().PaddingTop(5).LineHorizontal(1);

                                if (invoiceData.IsPaid)
                                {
                                    col.Item().PaddingTop(12).Background(Colors.Green.Lighten4).Padding(10)
                                        .Text("✓ This invoice has been paid. No further action required.")
                                        .Bold().FontColor(Colors.Green.Darken2);
                                }
                                else
                                {
                                    col.Item().PaddingTop(8).Text("Please remit payment within 30 days of invoice date.");
                                    if (!string.IsNullOrWhiteSpace(invoiceData.PaymentUrl))
                                    {
                                        col.Item().PaddingTop(10).Text("Pay online:").Bold();
                                        col.Item().PaddingTop(3).Text(invoiceData.PaymentUrl)
                                            .FontColor(Colors.Blue.Medium).FontSize(10);
                                    }
                                }
                                col.Item().PaddingTop(10).Text("For questions regarding this invoice, please contact the billing department.")
                                    .FontSize(10).FontColor(Colors.Grey.Medium);
                            });
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Page ").FontSize(9).FontColor(Colors.Grey.Medium);
                            x.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Medium);
                            x.Span(" of ").FontSize(9).FontColor(Colors.Grey.Medium);
                            x.TotalPages().FontSize(9).FontColor(Colors.Grey.Medium);
                        });
                });

                // ── Page 2+: Sessions detail ───────────────────────────────────────
                if (invoiceData.Sessions.Any())
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(1.5f, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10));

                        page.Header()
                            .PaddingBottom(10)
                            .Row(row =>
                            {
                                if (hasLogo)
                                    row.ConstantItem(80).Height(30).Image(logoPath);

                                row.RelativeItem().AlignMiddle().Column(col =>
                                {
                                    col.Item().Text($"Charging Sessions – {invoiceData.MonthName}")
                                        .Bold().FontSize(13).FontColor(Colors.Blue.Medium);
                                    col.Item().Text($"{invoiceData.InvoiceNumber}  ·  {invoiceData.UserName}")
                                        .FontSize(9).FontColor(Colors.Grey.Medium);
                                });
                            });

                        page.Content()
                            .PaddingTop(5)
                            .Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2.2f); // Start
                                    columns.RelativeColumn(2.2f); // End
                                    columns.RelativeColumn(1f);   // Station
                                    columns.RelativeColumn(1.3f); // kWh
                                    columns.RelativeColumn(1.3f); // Amount
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("Start").Bold().FontSize(9);
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("End").Bold().FontSize(9);
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("Station").Bold().FontSize(9).AlignCenter();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("kWh").Bold().FontSize(9).AlignRight();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("Amount").Bold().FontSize(9).AlignRight();
                                });

                                var rowIndex = 0;
                                foreach (var session in invoiceData.Sessions)
                                {
                                    var bg = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                                    table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                        .Text($"{session.Start:dd/MM HH:mm}").FontSize(9);
                                    table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                        .Text($"{session.End:dd/MM HH:mm}").FontSize(9);
                                    table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                        .Text($"{session.StationId}").FontSize(9).AlignCenter();
                                    table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                        .Text($"{session.ChargedKwh:F2}").FontSize(9).AlignRight();
                                    table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                        .Text($"€{session.Amount:F2}").FontSize(9).AlignRight();
                                }

                                // Totals row
                                table.Cell().ColumnSpan(3).Background(Colors.Grey.Lighten2).Padding(4)
                                    .Text("Total").Bold().FontSize(9);
                                table.Cell().Background(Colors.Grey.Lighten2).Padding(4)
                                    .Text($"{invoiceData.TotalKwh:F2}").Bold().FontSize(9).AlignRight();
                                table.Cell().Background(Colors.Grey.Lighten2).Padding(4)
                                    .Text($"€{invoiceData.TotalAmount:F2}").Bold().FontSize(9).AlignRight().FontColor(Colors.Blue.Medium);
                            });

                        page.Footer()
                            .AlignCenter()
                            .Text(x =>
                            {
                                x.Span("Page ").FontSize(9).FontColor(Colors.Grey.Medium);
                                x.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Medium);
                                x.Span(" of ").FontSize(9).FontColor(Colors.Grey.Medium);
                                x.TotalPages().FontSize(9).FontColor(Colors.Grey.Medium);
                            });
                    });
                }
            });

            return document.GeneratePdf();
        }
    }
}
