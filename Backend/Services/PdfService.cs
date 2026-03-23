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
            // Configure QuestPDF for commercial use (for development only)
            QuestPDF.Settings.License = LicenseType.Community;

            var document = Document.Create(container =>
            {
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
                            // Logo at top center
                            var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "zonnewijzer_logo.png");
                            if (File.Exists(logoPath))
                            {
                                column.Item().AlignCenter().Height(60).Image(logoPath);
                            }
                            
                            // Invoice title below logo
                            column.Item().PaddingTop(10).AlignCenter()
                                .Text("Charging Sessions Invoice")
                                .Bold().FontSize(24).FontColor(Colors.Blue.Medium);
                        });

                    page.Content()
                        .PaddingVertical(1, Unit.Centimetre)
                        .Column(column =>
                        {
                            // Invoice Header Information
                            column.Item().Row(row =>
                            {
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().Text($"Invoice Number: {invoiceData.InvoiceNumber}").Bold();
                                    col.Item().Text($"User: {invoiceData.UserName}");
                                    col.Item().Text($"Period: {invoiceData.MonthName}");
                                });

                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().AlignRight().Text($"Generated: {invoiceData.GeneratedAt:yyyy-MM-dd HH:mm}");
                                    col.Item().AlignRight().Text($"Rate: €{invoiceData.RatePerKwh:F2} per kWh").Bold();
                                });
                            });

                            column.Item().PaddingTop(20).LineHorizontal(1);

                            // Sessions Table
                            column.Item().PaddingTop(20).Text("Charging Sessions").Bold().FontSize(16);

                            if (invoiceData.Sessions.Any())
                            {
                                column.Item().PaddingTop(10).Table(table =>
                                {
                                    // Define columns
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(2); // Start
                                        columns.RelativeColumn(2); // End
                                        columns.RelativeColumn(1); // Station
                                        columns.RelativeColumn(1.5f); // kWh
                                        columns.RelativeColumn(1.5f); // Amount
                                    });

                                    // Header
                                    table.Header(header =>
                                    {
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Start").Bold().AlignCenter();
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("End").Bold().AlignCenter();
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Station").Bold().AlignCenter();
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("kWh").Bold().AlignCenter();
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Amount").Bold().AlignCenter();
                                    });

                                    // Data rows
                                    foreach (var session in invoiceData.Sessions)
                                    {
                                        table.Cell().Border(1).Padding(5).Text($"{session.Start:MM/dd HH:mm}");
                                        table.Cell().Border(1).Padding(5).Text($"{session.End:MM/dd HH:mm}");
                                        table.Cell().Border(1).Padding(5).Text($"{session.StationId}").AlignCenter();
                                        table.Cell().Border(1).Padding(5).Text($"{session.ChargedKwh:F2}").AlignRight();
                                        table.Cell().Border(1).Padding(5).Text($"€{session.Amount:F2}").AlignRight();
                                    }
                                });
                            }
                            else
                            {
                                column.Item().PaddingTop(10).Text("No charging sessions found for this period.").Italic();
                            }

                            // Summary
                            column.Item().PaddingTop(20).LineHorizontal(1);
                            
                            column.Item().PaddingTop(10).Row(row =>
                            {
                                row.RelativeItem().Text("");
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().Row(r =>
                                    {
                                        r.RelativeItem().Text("Total kWh:").FontSize(14);
                                        r.ConstantItem(100).AlignRight().Text($"{invoiceData.TotalKwh:F2}").FontSize(14);
                                    });
                                    
                                    col.Item().Row(r =>
                                    {
                                        r.RelativeItem().Text("Rate per kWh:").FontSize(14);
                                        r.ConstantItem(100).AlignRight().Text($"€{invoiceData.RatePerKwh:F2}").FontSize(14);
                                    });
                                    
                                    col.Item().PaddingTop(5).LineHorizontal(1);
                                    
                                    col.Item().PaddingTop(5).Row(r =>
                                    {
                                        r.RelativeItem().Text("Total Amount:").Bold().FontSize(16);
                                        r.ConstantItem(100).AlignRight().Text($"€{invoiceData.TotalAmount:F2}").Bold().FontSize(16).FontColor(Colors.Blue.Medium);
                                    });
                                });
                            });

                            // Payment Instructions
                            column.Item().PaddingTop(30).Column(col =>
                            {
                                col.Item().Text("Payment Instructions").Bold().FontSize(14);
                                col.Item().PaddingTop(5).Text("Please remit payment within 30 days of invoice date.");
                                col.Item().Text("For questions regarding this invoice, please contact the billing department.");
                            });
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text($"Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                        .FontSize(10).FontColor(Colors.Grey.Medium);
                });
            });

            return document.GeneratePdf();
        }
    }
}