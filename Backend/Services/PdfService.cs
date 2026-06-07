using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QRCoder;
using System.IO;


namespace Backend.Services
{
    public class PdfService : IPdfService
    {
        /// <summary>NL statutory VAT; all invoice line amounts and totals are inclusive of this rate.</summary>
        private const decimal VatRate = 0.21m;

        private const string SupplierKvK = "68871333";
        private const string SupplierBtwId = "NL857626693B01";
        private const string CustomerPortalUrl = "https://zonnewijzer.zonneklaar-consulting.com/";

        private sealed record InvoicePdfLabels(
            string Title,
            string InvoiceNumber,
            string Period,
            string Generated,
            string TotalKwh,
            string RatePerKwh,
            string VatNote,
            string SubtotalExclVat,
            string VatLine,
            string TotalInclVat,
            string PaymentTitle,
            string PaymentPaid,
            string PaymentDue,
            string PayOnline,
            string QrHint,
            string ScanToPay,
            string PaymentQuestions,
            string PortalPrefix,
            string PaymentRequestDue,
            string PaymentRequestNotice,
            string PaymentRequestQuestions,
            string SessionsHeader,
            string ColStart,
            string ColEnd,
            string ColStation,
            string ColConnector,
            string ColAmount,
            string SessionOngoing,
            string SessionCardPrefix,
            string TableTotal,
            string TableVatNote,
            string Page,
            string PageOf);

        private static InvoicePdfLabels LabelsFor(bool dutch) =>
            dutch
                ? new InvoicePdfLabels(
                    Title: "Factuur laadsessies",
                    InvoiceNumber: "Factuurnummer:",
                    Period: "Periode:",
                    Generated: "Aangemaakt:",
                    TotalKwh: "Totaal kWh:",
                    RatePerKwh: "Tarief per kWh:",
                    VatNote: "Tarief en bedragen zijn inclusief 21% BTW.",
                    SubtotalExclVat: "Subtotaal (excl. BTW):",
                    VatLine: "BTW (21%):",
                    TotalInclVat: "Totaal (incl. BTW):",
                    PaymentTitle: "Betaling",
                    PaymentPaid: "✓ Deze factuur is betaald. Er is geen actie vereist.",
                    PaymentDue: "Gelieve te betalen binnen 30 dagen na factuurdatum.",
                    PayOnline: "Betaal online:",
                    QrHint: "Scannen kan ook via de QR-code.",
                    ScanToPay: "Scan om te betalen",
                    PaymentQuestions: "Voor vragen over deze factuur kunt u contact opnemen met de administratie.",
                    PortalPrefix: "Bekijk facturen en betaalstatus online:",
                    PaymentRequestDue: "Gelieve te betalen binnen 30 dagen na factuurdatum.",
                    PaymentRequestNotice: "U ontvangt apart een betalingsverzoek voor dit bedrag.",
                    PaymentRequestQuestions: "Voor vragen over deze factuur kunt u contact opnemen met de administratie.",
                    SessionsHeader: "Laadsessies",
                    ColStart: "Start",
                    ColEnd: "Einde",
                    ColStation: "Laadpaal",
                    ColConnector: "Conn.",
                    ColAmount: "Bedrag (incl. BTW)",
                    SessionOngoing: "Bezig",
                    SessionCardPrefix: "Pas",
                    TableTotal: "Totaal",
                    TableVatNote: "Alle bedragen in deze tabel zijn inclusief 21% BTW.",
                    Page: "Pagina",
                    PageOf: "van")
                : new InvoicePdfLabels(
                    Title: "Charging Sessions Invoice",
                    InvoiceNumber: "Invoice Number:",
                    Period: "Period:",
                    Generated: "Generated:",
                    TotalKwh: "Total kWh:",
                    RatePerKwh: "Rate per kWh:",
                    VatNote: "Rate and line amounts include 21% VAT (BTW).",
                    SubtotalExclVat: "Subtotal (excl. VAT):",
                    VatLine: "VAT (21%):",
                    TotalInclVat: "Total (incl. VAT):",
                    PaymentTitle: "Payment",
                    PaymentPaid: "✓ This invoice has been paid. No further action required.",
                    PaymentDue: "Please remit payment within 30 days of invoice date.",
                    PayOnline: "Pay online:",
                    QrHint: "If scanning is easier, use the QR code.",
                    ScanToPay: "Scan to pay",
                    PaymentQuestions: "For questions regarding this invoice, please contact the billing department.",
                    PortalPrefix: "View invoices and payment status online:",
                    PaymentRequestDue: "Gelieve te betalen binnen 30 dagen na factuurdatum.",
                    PaymentRequestNotice: "U ontvangt apart een betalingsverzoek voor dit bedrag.",
                    PaymentRequestQuestions: "Voor vragen over deze factuur kunt u contact opnemen met de administratie.",
                    SessionsHeader: "Charging Sessions",
                    ColStart: "Start",
                    ColEnd: "End",
                    ColStation: "Station",
                    ColConnector: "Conn.",
                    ColAmount: "Amount (incl. VAT)",
                    SessionOngoing: "Ongoing",
                    SessionCardPrefix: "Card",
                    TableTotal: "Total",
                    TableVatNote: "All amounts in this table include 21% VAT.",
                    Page: "Page",
                    PageOf: "of");

        private static (decimal ExclVat, decimal VatAmount) SplitVatInclusive(decimal totalInclVat)
        {
            var excl = Math.Round(totalInclVat / (1 + VatRate), 2, MidpointRounding.AwayFromZero);
            var vat = Math.Round(totalInclVat - excl, 2, MidpointRounding.AwayFromZero);
            return (excl, vat);
        }

        private static byte[]? TryGeneratePaymentQrCode(string? paymentUrl)
        {
            if (string.IsNullOrWhiteSpace(paymentUrl))
                return null;

            if (!Uri.TryCreate(paymentUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(paymentUrl, QRCodeGenerator.ECCLevel.Q);
            var pngQrCode = new PngByteQRCode(qrData);
            return pngQrCode.GetGraphic(8);
        }

        private static void ComposeInvoiceFooter(PageDescriptor page, InvoicePdfLabels labels)
        {
            page.Footer().Column(col =>
            {
                col.Item().AlignCenter()
                    .Text($"KvK {SupplierKvK}  ·  BTW-id {SupplierBtwId}")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(2).AlignCenter()
                    .Text(text =>
                    {
                        text.Span($"{labels.Page} ").FontSize(9).FontColor(Colors.Grey.Medium);
                        text.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Medium);
                        text.Span($" {labels.PageOf} ").FontSize(9).FontColor(Colors.Grey.Medium);
                        text.TotalPages().FontSize(9).FontColor(Colors.Grey.Medium);
                    });
            });
        }

        public byte[] GenerateInvoicePdf(InvoiceData invoiceData)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var labels = LabelsFor(invoiceData.UsePaymentRequestNotice);
            var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "zonnewijzer_logo.png");
            var hasLogo = File.Exists(logoPath);
            var (totalExclVat, vatAmount) = SplitVatInclusive(invoiceData.TotalAmount);
            var paymentQrCode = TryGeneratePaymentQrCode(invoiceData.PaymentUrl);

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
                            if (hasLogo)
                                column.Item().AlignCenter().Height(60).Image(logoPath);

                            column.Item().PaddingTop(10).AlignCenter()
                                .Text(labels.Title)
                                .Bold().FontSize(24).FontColor(Colors.Blue.Medium);
                        });

                    page.Content()
                        .PaddingVertical(1, Unit.Centimetre)
                        .Column(column =>
                        {
                            column.Item().Row(row =>
                            {
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().Text($"{labels.InvoiceNumber} {invoiceData.InvoiceNumber}").Bold();
                                    col.Item().Text($"{labels.Period} {invoiceData.MonthName}");
                                    col.Item().PaddingTop(5).Text($"{labels.Generated} {invoiceData.GeneratedAt:yyyy-MM-dd HH:mm}");
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
                                        r.RelativeItem().Text(labels.TotalKwh).FontSize(fsLine);
                                        r.ConstantItem(valueW).AlignRight().Text($"{invoiceData.TotalKwh:F2} kWh").FontSize(fsLine);
                                    });
                                    col.Item().PaddingTop(4).Row(r =>
                                    {
                                        r.RelativeItem().Text(labels.RatePerKwh).FontSize(fsLine);
                                        r.ConstantItem(valueW).AlignRight().Text($"€{invoiceData.RatePerKwh:F4}").FontSize(fsLine);
                                    });
                                    col.Item().PaddingTop(3).Text(labels.VatNote)
                                        .FontSize(fsNote).FontColor(Colors.Grey.Darken1).Italic();
                                    col.Item().PaddingTop(8).LineHorizontal(0.75f);
                                    col.Item().PaddingTop(6).Row(r =>
                                    {
                                        r.RelativeItem().Text(labels.SubtotalExclVat).FontSize(fsLine);
                                        r.ConstantItem(valueW).AlignRight().Text($"€{totalExclVat:F2}").FontSize(fsLine);
                                    });
                                    col.Item().PaddingTop(4).Row(r =>
                                    {
                                        r.RelativeItem().Text(labels.VatLine).FontSize(fsLine);
                                        r.ConstantItem(valueW).AlignRight().Text($"€{vatAmount:F2}").FontSize(fsLine);
                                    });
                                    col.Item().PaddingTop(6).LineHorizontal(0.75f);
                                    col.Item().PaddingTop(6).Row(r =>
                                    {
                                        r.RelativeItem().Text(labels.TotalInclVat).Bold().FontSize(fsTotal);
                                        r.ConstantItem(valueW).AlignRight().Text($"€{invoiceData.TotalAmount:F2}").Bold().FontSize(fsTotal).FontColor(Colors.Blue.Medium);
                                    });
                                });
                                outer.RelativeItem(1);
                            });

                            column.Item().PaddingTop(40).Column(col =>
                            {
                                if (invoiceData.UsePaymentRequestNotice)
                                {
                                    col.Item().Text(labels.PaymentTitle).Bold().FontSize(14);
                                    col.Item().PaddingTop(5).LineHorizontal(1);
                                    col.Item().PaddingTop(8).Text(labels.PaymentRequestDue);
                                    col.Item().PaddingTop(8).Text(labels.PaymentRequestNotice);
                                    col.Item().PaddingTop(10).Text(labels.PaymentRequestQuestions)
                                        .FontSize(10).FontColor(Colors.Grey.Medium);
                                }
                                else
                                {
                                    col.Item().Text(labels.PaymentTitle).Bold().FontSize(14);
                                    col.Item().PaddingTop(5).LineHorizontal(1);

                                    if (invoiceData.IsPaid)
                                    {
                                        col.Item().PaddingTop(12).Background(Colors.Green.Lighten4).Padding(10)
                                            .Text(labels.PaymentPaid)
                                            .Bold().FontColor(Colors.Green.Darken2);
                                    }
                                    else
                                    {
                                        col.Item().PaddingTop(8).Text(labels.PaymentDue);
                                        if (!string.IsNullOrWhiteSpace(invoiceData.PaymentUrl))
                                        {
                                            col.Item().PaddingTop(10).Text(labels.PayOnline).Bold();
                                            col.Item().PaddingTop(6).Row(row =>
                                            {
                                                row.RelativeItem().Column(linkCol =>
                                                {
                                                    linkCol.Item().Text(invoiceData.PaymentUrl)
                                                        .FontColor(Colors.Blue.Medium).FontSize(10);
                                                    linkCol.Item().PaddingTop(3).Text(labels.QrHint)
                                                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                                                });

                                                if (paymentQrCode != null)
                                                {
                                                    row.ConstantItem(120).AlignRight().Column(qrCol =>
                                                    {
                                                        qrCol.Item().AlignCenter().Text(labels.ScanToPay).FontSize(9).Bold();
                                                        qrCol.Item().PaddingTop(4).Width(100).Height(100).Image(paymentQrCode);
                                                    });
                                                }
                                            });
                                        }
                                    }
                                    col.Item().PaddingTop(10).Text(labels.PaymentQuestions)
                                        .FontSize(10).FontColor(Colors.Grey.Medium);
                                    col.Item().PaddingTop(6).Text(text =>
                                    {
                                        text.Span($"{labels.PortalPrefix} ").FontSize(10).FontColor(Colors.Grey.Medium);
                                        text.Hyperlink(CustomerPortalUrl, CustomerPortalUrl.TrimEnd('/'))
                                            .FontSize(10).FontColor(Colors.Blue.Medium);
                                    });
                                }
                            });
                        });

                    ComposeInvoiceFooter(page, labels);
                });

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
                                    col.Item().Text($"{labels.SessionsHeader} – {invoiceData.MonthName}")
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
                                        columns.RelativeColumn(2f);
                                        columns.RelativeColumn(2f);
                                        columns.RelativeColumn(2.2f);
                                        columns.RelativeColumn(0.65f);
                                        columns.RelativeColumn(1.15f);
                                        columns.RelativeColumn(1.15f);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text(labels.ColStart).Bold().FontSize(9);
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text(labels.ColEnd).Bold().FontSize(9);
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text(labels.ColStation).Bold().FontSize(9);
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text(labels.ColConnector).Bold().FontSize(9).AlignCenter();
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("kWh").Bold().FontSize(9).AlignRight();
                                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text(labels.ColAmount).Bold().FontSize(9).AlignRight();
                                    });

                                    var rowIndex = 0;
                                    foreach (var session in invoiceData.Sessions)
                                    {
                                        var rowBackground = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                                        table.Cell().Background(rowBackground).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                            .Text($"{session.Start:dd/MM HH:mm}").FontSize(9);
                                        table.Cell().Background(rowBackground).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                            .Text(session.End.HasValue ? $"{session.End.Value:dd/MM HH:mm}" : labels.SessionOngoing).FontSize(9);
                                        table.Cell().Background(rowBackground).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                            .Column(col =>
                                            {
                                                var title = string.IsNullOrWhiteSpace(session.ChargingStationName)
                                                    ? $"#{session.StationId}"
                                                    : session.ChargingStationName;
                                                col.Item().Text(title).FontSize(9);
                                                if (!string.IsNullOrWhiteSpace(session.StationSerialNumber))
                                                    col.Item().Text($"S/N {session.StationSerialNumber}").FontSize(8).FontColor(Colors.Grey.Darken1);
                                                if (session.ChargeCardId != 0)
                                                    col.Item().Text($"{labels.SessionCardPrefix} {session.ChargeCardId}").FontSize(8).FontColor(Colors.Grey.Darken1);
                                            });
                                        table.Cell().Background(rowBackground).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                            .Text(string.IsNullOrWhiteSpace(session.Connector) ? "—" : session.Connector).FontSize(9).AlignCenter();
                                        table.Cell().Background(rowBackground).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                            .Text($"{session.ChargedKwh:F2}").FontSize(9).AlignRight();
                                        table.Cell().Background(rowBackground).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                            .Text($"€{session.Amount:F2}").FontSize(9).AlignRight();
                                    }

                                    table.Cell().ColumnSpan(4).Background(Colors.Grey.Lighten2).Padding(4)
                                        .Text(labels.TableTotal).Bold().FontSize(9);
                                    table.Cell().Background(Colors.Grey.Lighten2).Padding(4)
                                        .Text($"{invoiceData.TotalKwh:F2}").Bold().FontSize(9).AlignRight();
                                    table.Cell().Background(Colors.Grey.Lighten2).Padding(4)
                                        .Text($"€{invoiceData.TotalAmount:F2}").Bold().FontSize(9).AlignRight().FontColor(Colors.Blue.Medium);
                                });

                                detailCol.Item().PaddingTop(8)
                                    .Text(labels.TableVatNote)
                                    .FontSize(9).FontColor(Colors.Grey.Darken1).Italic();
                            });

                        ComposeInvoiceFooter(page, labels);
                    });
                }
            });

            return document.GeneratePdf();
        }
    }
}
