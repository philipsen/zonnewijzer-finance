using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;


namespace Backend.Services
{
    public class PdfService : IPdfService
    {
        /// <summary>NL statutory VAT; all invoice line amounts and totals are inclusive of this rate.</summary>
        private const decimal VatRate = 0.21m;

        private const string SupplierKvK = "50323342";
        private const string SupplierBtwId = "NL003422577B06";

        private static (decimal ExclVat, decimal VatAmount) SplitVatInclusive(decimal totalInclVat)
        {
            var excl = Math.Round(totalInclVat / (1 + VatRate), 2, MidpointRounding.AwayFromZero);
            var vat = Math.Round(totalInclVat - excl, 2, MidpointRounding.AwayFromZero);
            return (excl, vat);
        }

        private static void ComposeInvoiceFooter(PageDescriptor page)
        {
            page.Footer().Column(col =>
            {
                col.Item().AlignCenter()
                    .Text($"KvK {SupplierKvK}  ·  BTW-id {SupplierBtwId}")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(2).AlignCenter()
                    .Text(text =>
                    {
                        text.Span("Page ").FontSize(9).FontColor(Colors.Grey.Medium);
                        text.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Medium);
                        text.Span(" of ").FontSize(9).FontColor(Colors.Grey.Medium);
                        text.TotalPages().FontSize(9).FontColor(Colors.Grey.Medium);
                    });
            });
        }

        public byte[] GenerateInvoicePdf(InvoiceData invoiceData)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "zonnewijzer_logo.png");
            var hasLogo = File.Exists(logoPath);
            var (totalExclVat, vatAmount) = SplitVatInclusive(invoiceData.TotalAmount);

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
                                    col.Item().PaddingTop(10).Text($"KvK {SupplierKvK}  ·  BTW-id {SupplierBtwId}")
                                        .FontSize(9).FontColor(Colors.Grey.Darken1);
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

                            // Summary totals: 3/4 content width, compact type (avoids mid-label wraps)
                            column.Item().PaddingTop(30).Row(outer =>
                            {
                                outer.RelativeItem(1);
                                outer.RelativeItem(6).Column(col =>
                                {
                                    const int fsLine = 10;
                                    const int fsNote = 8;
                                    const int fsTotal = 12;
                                    const float valueW = 92;

                                    col.Item().Row(r =>
                                    {
                                        r.RelativeItem().Text("Total kWh:").FontSize(fsLine);
                                        r.ConstantItem(valueW).AlignRight().Text($"{invoiceData.TotalKwh:F2} kWh").FontSize(fsLine);
                                    });
                                    col.Item().PaddingTop(4).Row(r =>
                                    {
                                        r.RelativeItem().Text("Rate per kWh:").FontSize(fsLine);
                                        r.ConstantItem(valueW).AlignRight().Text($"€{invoiceData.RatePerKwh:F4}").FontSize(fsLine);
                                    });
                                    col.Item().PaddingTop(3).Text("Rate and line amounts include 21% VAT (BTW).")
                                        .FontSize(fsNote).FontColor(Colors.Grey.Darken1).Italic();
                                    col.Item().PaddingTop(8).LineHorizontal(0.75f);
                                    col.Item().PaddingTop(6).Row(r =>
                                    {
                                        r.RelativeItem().Text("Subtotal (excl. VAT):").FontSize(fsLine);
                                        r.ConstantItem(valueW).AlignRight().Text($"€{totalExclVat:F2}").FontSize(fsLine);
                                    });
                                    col.Item().PaddingTop(4).Row(r =>
                                    {
                                        r.RelativeItem().Text("VAT (21%):").FontSize(fsLine);
                                        r.ConstantItem(valueW).AlignRight().Text($"€{vatAmount:F2}").FontSize(fsLine);
                                    });
                                    col.Item().PaddingTop(6).LineHorizontal(0.75f);
                                    col.Item().PaddingTop(6).Row(r =>
                                    {
                                        r.RelativeItem().Text("Total (incl. VAT):").Bold().FontSize(fsTotal);
                                        r.ConstantItem(valueW).AlignRight().Text($"€{invoiceData.TotalAmount:F2}").Bold().FontSize(fsTotal).FontColor(Colors.Blue.Medium);
                                    });
                                });
                                outer.RelativeItem(1);
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

                    ComposeInvoiceFooter(page);
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
                            .Column(detailCol =>
                            {
                                detailCol.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(2f); // Start
                                        columns.RelativeColumn(2f); // End
                                        columns.RelativeColumn(2.2f); // Station name + serial
                                        columns.RelativeColumn(0.65f); // Connector
                                        columns.RelativeColumn(1.15f); // kWh
                                        columns.RelativeColumn(1.15f); // Amount
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("Start").Bold().FontSize(9);
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("End").Bold().FontSize(9);
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("Station").Bold().FontSize(9);
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("Conn.").Bold().FontSize(9).AlignCenter();
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("kWh").Bold().FontSize(9).AlignRight();
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("Amount (incl. VAT)").Bold().FontSize(9).AlignRight();
                                    });

                                    var rowIndex = 0;
                                    foreach (var session in invoiceData.Sessions)
                                    {
                                        var rowBackground = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                                        table.Cell().Background(rowBackground).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                            .Text($"{session.Start:dd/MM HH:mm}").FontSize(9);
                                        table.Cell().Background(rowBackground).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                            .Text(session.End.HasValue ? $"{session.End.Value:dd/MM HH:mm}" : "Ongoing").FontSize(9);
                                        table.Cell().Background(rowBackground).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                            .Column(col =>
                                            {
                                                var title = string.IsNullOrWhiteSpace(session.ChargingStationName)
                                                    ? $"#{session.StationId}"
                                                    : session.ChargingStationName;
                                                col.Item().Text(title).FontSize(9);
                                                if (!string.IsNullOrWhiteSpace(session.StationSerialNumber))
                                                    col.Item().Text($"S/N {session.StationSerialNumber}").FontSize(8).FontColor(Colors.Grey.Darken1);
                                            });
                                        table.Cell().Background(rowBackground).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                            .Text(string.IsNullOrWhiteSpace(session.Connector) ? "—" : session.Connector).FontSize(9).AlignCenter();
                                        table.Cell().Background(rowBackground).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                            .Text($"{session.ChargedKwh:F2}").FontSize(9).AlignRight();
                                        table.Cell().Background(rowBackground).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                            .Text($"€{session.Amount:F2}").FontSize(9).AlignRight();
                                    }

                                    // Totals row
                                    table.Cell().ColumnSpan(4).Background(Colors.Grey.Lighten2).Padding(4)
                                        .Text("Total").Bold().FontSize(9);
                                    table.Cell().Background(Colors.Grey.Lighten2).Padding(4)
                                        .Text($"{invoiceData.TotalKwh:F2}").Bold().FontSize(9).AlignRight();
                                    table.Cell().Background(Colors.Grey.Lighten2).Padding(4)
                                        .Text($"€{invoiceData.TotalAmount:F2}").Bold().FontSize(9).AlignRight().FontColor(Colors.Blue.Medium);
                                });

                                detailCol.Item().PaddingTop(8)
                                    .Text("All amounts in this table include 21% VAT.")
                                    .FontSize(9).FontColor(Colors.Grey.Darken1).Italic();
                            });

                        ComposeInvoiceFooter(page);
                    });
                }
            });

            return document.GeneratePdf();
        }
    }
}
