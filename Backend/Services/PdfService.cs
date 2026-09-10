using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QRCoder;
using System.IO;

namespace Backend.Services
{
    public class PdfService : IPdfService
    {
        private const string SupplierKvK = "68871333";
        private const string SupplierBtwId = "NL857626693B01";
        private const string SupplierName = "DEELSTROOM DELFT";
        private const string SupplierProject = "Project ZonneWijzer";
        private const string SupplierStreet = "Schieweg 15A-38";
        private const string SupplierPostalCity = "2627 AN Delft";
        private const string BillingEmail = "ZonneWijzer@Warmoestuin.nl";
        private const string CustomerPortalUrl = "https://zonnewijzer.zonneklaar-consulting.com/";

        private const string SupplierIban = "NL40TRIO0321247604";
        private const string SupplierBic = "TRIONL2U";
        private const string SupplierBankAccountName = "Coöperatie Deelstroom Delft U.A.";

        private const string BrandBlue = "#1D78BE";
        private const string MutedGrey = "#9F9F9F";
        private const string PaymentBoxBackground = "#E3F0F8";

        // Marketing template: lower-left quarter of sun icon, ~82pt, flush to page corner.
        private const float SunCornerSizePt = 82f;

        private sealed record InvoicePdfLabels(
            string Title,
            string InvoiceNumber,
            string Period,
            string InvoiceDate,
            string DueDate,
            string ColDescription,
            string ColQuantity,
            string ColRate,
            string ColTotal,
            string LineKwh,
            string SubtotalExclVat,
            string VatLine,
            string Total,
            string PaymentTitle,
            string PaymentPaid,
            string PaymentDue,
            string PayOnline,
            string QrHint,
            string ScanToPay,
            string PaymentRequestNotice,
            string PaymentContact,
            string PortalPrefix,
            string SessionsTitle,
            string ColStart,
            string ColEnd,
            string ColStation,
            string ColKwh,
            string ColAmount,
            string SessionOngoing,
            string SessionCardPrefix);

        private static InvoicePdfLabels LabelsFor(bool dutch) =>
            dutch
                ? new InvoicePdfLabels(
                    Title: "Factuur laadsessies",
                    InvoiceNumber: "Factuurnummer:",
                    Period: "Periode:",
                    InvoiceDate: "Factuurdatum:",
                    DueDate: "Vervaldatum:",
                    ColDescription: "Omschrijving",
                    ColQuantity: "Aantal",
                    ColRate: "Tarief (incl. Btw)",
                    ColTotal: "Totaal",
                    LineKwh: "KWh",
                    SubtotalExclVat: "Subtotaal excl BTW",
                    VatLine: "BTW (21%)",
                    Total: "Totaal",
                    PaymentTitle: "Betaling",
                    PaymentPaid: "✓ Deze factuur is betaald. Er is geen actie vereist.",
                    PaymentDue: "Gelieve te betalen binnen 30 dagen na factuurdatum.",
                    PayOnline: "Betaal online:",
                    QrHint: "Scannen kan ook via de QR-code.",
                    ScanToPay: "Scan om te betalen",
                    PaymentRequestNotice: "Gelieve het bedrag over te maken op rekening NL40 TRIO 0321 2476 04, t.n.v. Coöperatie Deelstroom Delft U.A., onder vermelding van het factuurnummer.",
                    PaymentContact: $"Voor vragen over deze factuur kunt u contact opnemen met de administratie o.v.v. factuurnummer via {BillingEmail}",
                    PortalPrefix: "Bekijk facturen en betaalstatus online:",
                    SessionsTitle: "Specificatie factuurnummer:",
                    ColStart: "Start",
                    ColEnd: "Einde",
                    ColStation: "Laadpaal",
                    ColKwh: "kWh",
                    ColAmount: "Bedrag (incl. BTW)",
                    SessionOngoing: "Bezig",
                    SessionCardPrefix: "Pas")
                : new InvoicePdfLabels(
                    Title: "Charging Sessions Invoice",
                    InvoiceNumber: "Invoice number:",
                    Period: "Period:",
                    InvoiceDate: "Invoice date:",
                    DueDate: "Due date:",
                    ColDescription: "Description",
                    ColQuantity: "Quantity",
                    ColRate: "Rate (incl. VAT)",
                    ColTotal: "Total",
                    LineKwh: "kWh",
                    SubtotalExclVat: "Subtotal excl. VAT",
                    VatLine: "VAT (21%)",
                    Total: "Total",
                    PaymentTitle: "Payment",
                    PaymentPaid: "✓ This invoice has been paid. No further action required.",
                    PaymentDue: "Please remit payment within 30 days of invoice date.",
                    PayOnline: "Pay online:",
                    QrHint: "If scanning is easier, use the QR code.",
                    ScanToPay: "Scan to pay",
                    PaymentRequestNotice: "You will receive a separate payment request for this amount.",
                    PaymentContact: $"For questions regarding this invoice, please contact billing quoting the invoice number at {BillingEmail}",
                    PortalPrefix: "View invoices and payment status online:",
                    SessionsTitle: "Session detail invoice number:",
                    ColStart: "Start",
                    ColEnd: "End",
                    ColStation: "Charging point",
                    ColKwh: "kWh",
                    ColAmount: "Amount (incl. VAT)",
                    SessionOngoing: "Ongoing",
                    SessionCardPrefix: "Card");

        private static IContainer BodyText(IContainer container) =>
            container.DefaultTextStyle(x => x.FontFamily(InvoiceFontSetup.Family).FontSize(11).FontColor(Colors.Black));

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

        private static string BuildSepaEpcPayload(
            string iban,
            string beneficiaryName,
            decimal amountEur,
            string remittanceText,
            string? bic = null)
        {
            static string TrimMax(string value, int max) =>
                value.Length <= max ? value : value[..max];

            var cleanIban = new string(iban.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
            var name = TrimMax(beneficiaryName.Trim(), 70);
            var remittance = TrimMax(remittanceText.Trim(), 140);
            var amount = Math.Round(amountEur, 2, MidpointRounding.AwayFromZero);

            // EPC QR (Belgian/Dutch banking apps): "SCT" credit transfer.
            // Line endings must be LF, not CRLF.
            // Fields:
            //  1 Service tag
            //  2 Version
            //  3 Character set
            //  4 Identification (SCT)
            //  5 BIC (optional but recommended)
            //  6 Beneficiary name
            //  7 IBAN
            //  8 Amount (EUR)
            //  9 Purpose (optional)
            // 10 Remittance information (unstructured)
            // 11 Information (optional)
            var lines = new List<string>
            {
                "BCD",
                "002",
                "1",
                "SCT",
                bic?.Trim() ?? string.Empty,
                name,
                cleanIban,
                $"EUR{amount:0.00}",
                string.Empty,
                remittance,
                string.Empty
            };

            return string.Join("\n", lines);
        }

        private static byte[]? TryGenerateSepaTransferQrCode(InvoiceData invoiceData)
        {
            // Only generate for bank-transfer style invoices.
            if (!invoiceData.UsePaymentRequestNotice)
                return null;

            if (invoiceData.TotalAmount <= 0)
                return null;

            var payload = BuildSepaEpcPayload(
                iban: SupplierIban,
                beneficiaryName: SupplierBankAccountName,
                amountEur: invoiceData.TotalAmount,
                remittanceText: invoiceData.InvoiceNumber,
                bic: SupplierBic);

            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            var pngQrCode = new PngByteQRCode(qrData);
            return pngQrCode.GetGraphic(8);
        }

        private static (string? Logo, string? SunCorner) ResolveBrandAssets()
        {
            var assets = Path.Combine(Directory.GetCurrentDirectory(), "Assets");
            var logo = Path.Combine(assets, "zonnewijzer_logo.png");
            var sunCorner = Path.Combine(assets, "zonnewijzer_sun_corner.png");
            return (
                File.Exists(logo) ? logo : null,
                File.Exists(sunCorner) ? sunCorner : null);
        }

        private static void ComposeSunCorner(IContainer container, string sunCornerPath)
        {
            container
                .AlignRight()
                .AlignTop()
                .Width(SunCornerSizePt)
                .Height(SunCornerSizePt)
                .Image(sunCornerPath);
        }

        private static void ComposeBrandHeader(IContainer container, string? logoPath, string? subtitle = null)
        {
            container.Column(col =>
            {
                if (!string.IsNullOrEmpty(logoPath))
                    col.Item().AlignCenter().Height(28).Image(logoPath);

                if (!string.IsNullOrWhiteSpace(subtitle))
                {
                    col.Item().PaddingTop(10).Text(subtitle)
                        .FontFamily(InvoiceFontSetup.Family).Bold().FontSize(12).FontColor(BrandBlue);
                }
            });
        }

        private static void ComposeSupplierFooter(IContainer container)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Cell().BorderTop(1).BorderColor(BrandBlue).PaddingTop(8).PaddingRight(8)
                    .Column(left =>
                    {
                        left.Item().Text($"KVK: {SupplierKvK}").FontFamily(InvoiceFontSetup.Family).FontSize(11);
                        left.Item().Text($"BTW: {SupplierBtwId}").FontFamily(InvoiceFontSetup.Family).FontSize(11);
                    });

                table.Cell().BorderTop(1).BorderColor(BrandBlue).PaddingTop(8).PaddingLeft(8)
                    .Column(right =>
                    {
                        right.Item().Text(SupplierName).FontFamily(InvoiceFontSetup.Family).FontSize(11);
                        right.Item().Text(SupplierProject).FontFamily(InvoiceFontSetup.Family).FontSize(11);
                        right.Item().Text(SupplierStreet).FontFamily(InvoiceFontSetup.Family).FontSize(11);
                        right.Item().Text(SupplierPostalCity).FontFamily(InvoiceFontSetup.Family).FontSize(11);
                    });
            });
        }

        private static void ComposeInvoiceMetaTable(
            IContainer container,
            InvoicePdfLabels labels,
            InvoiceData invoiceData,
            DateTime dueDate)
        {
            var customerLines = new[]
            {
                invoiceData.UserName,
                BuildStreetLine(invoiceData),
                invoiceData.PostalCode ?? string.Empty,
                invoiceData.PlaceName ?? string.Empty
            };

            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(105);
                    columns.RelativeColumn(1.1f);
                    columns.ConstantColumn(24);
                    columns.RelativeColumn(1.3f);
                });

                void MetaRow(int rowIndex, string label, string value, bool labelBold)
                {
                    var topBorder = rowIndex == 0;
                    table.Cell().Element(c =>
                    {
                        if (topBorder) c = c.BorderTop(1).BorderColor(BrandBlue);
                        var text = c.PaddingVertical(4).Text(label).FontFamily(InvoiceFontSetup.Family).FontSize(11);
                        if (labelBold) text.Bold();
                    });
                    table.Cell().Element(c =>
                    {
                        if (topBorder) c = c.BorderTop(1).BorderColor(BrandBlue);
                        c.PaddingVertical(4).Text(value).FontFamily(InvoiceFontSetup.Family).FontSize(11);
                    });
                    table.Cell().Element(c =>
                    {
                        if (topBorder) c = c.BorderTop(1).BorderColor(BrandBlue);
                        c.PaddingVertical(4).Text(string.Empty);
                    });
                    table.Cell().Element(c =>
                    {
                        if (topBorder) c = c.BorderTop(1).BorderColor(BrandBlue);
                        c.PaddingVertical(4).AlignRight().Text(customerLines[rowIndex])
                            .FontFamily(InvoiceFontSetup.Family).FontSize(11);
                    });
                }

                MetaRow(0, labels.InvoiceNumber, invoiceData.InvoiceNumber, labelBold: true);
                MetaRow(1, labels.Period, invoiceData.MonthName, labelBold: true);
                MetaRow(2, labels.InvoiceDate, $"{invoiceData.GeneratedAt:yyyy-MM-dd HH:mm}", labelBold: false);
                MetaRow(3, labels.DueDate, $"{dueDate:yyyy-MM-dd}", labelBold: false);
            });
        }

        private static void ComposeInvoiceSummaryTable(
            IContainer container,
            InvoicePdfLabels labels,
            InvoiceData invoiceData,
            decimal totalExclVat,
            decimal vatAmount)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2.5f);  // Omschrijving / labels
                    columns.RelativeColumn(0.9f); // Aantal (numeric)
                    columns.RelativeColumn(1.15f); // Tarief €0.0000
                    columns.RelativeColumn(1.15f); // Totaal €0.00
                });

                table.Cell().BorderBottom(1).BorderColor(BrandBlue).PaddingBottom(6)
                    .Text(labels.ColDescription).FontFamily(InvoiceFontSetup.Family).Bold().FontSize(11).FontColor(BrandBlue);
                table.Cell().BorderBottom(1).BorderColor(BrandBlue).PaddingBottom(6).AlignRight()
                    .Text(labels.ColQuantity).FontFamily(InvoiceFontSetup.Family).Bold().FontSize(11).FontColor(BrandBlue);
                table.Cell().BorderBottom(1).BorderColor(BrandBlue).PaddingBottom(6).AlignRight()
                    .Text(labels.ColRate).FontFamily(InvoiceFontSetup.Family).Bold().FontSize(11).FontColor(BrandBlue);
                table.Cell().BorderBottom(1).BorderColor(BrandBlue).PaddingBottom(6).AlignRight()
                    .Text(labels.ColTotal).FontFamily(InvoiceFontSetup.Family).Bold().FontSize(11).FontColor(BrandBlue);

                table.Cell().PaddingTop(4).Text(labels.LineKwh).FontFamily(InvoiceFontSetup.Family).FontSize(11);
                table.Cell().PaddingTop(4).AlignRight().Text($"{invoiceData.TotalKwh:F2}").FontFamily(InvoiceFontSetup.Family).FontSize(11);
                table.Cell().PaddingTop(4).AlignRight().Text($"€{invoiceData.RatePerKwh:F4}").FontFamily(InvoiceFontSetup.Family).FontSize(11);
                table.Cell().PaddingTop(4).AlignRight().Text($"€{invoiceData.TotalAmount:F2}").FontFamily(InvoiceFontSetup.Family).FontSize(11);

                table.Cell().PaddingTop(8).Text(labels.SubtotalExclVat).FontFamily(InvoiceFontSetup.Family).FontSize(11);
                table.Cell().ColumnSpan(2);
                table.Cell().PaddingTop(8).AlignRight().Text($"€{totalExclVat:F2}").FontFamily(InvoiceFontSetup.Family).FontSize(11);

                table.Cell().PaddingTop(4).Text(labels.VatLine).FontFamily(InvoiceFontSetup.Family).FontSize(11);
                table.Cell().ColumnSpan(2);
                table.Cell().PaddingTop(4).AlignRight().Text($"€{vatAmount:F2}").FontFamily(InvoiceFontSetup.Family).FontSize(11);

                table.Cell().BorderTop(1).BorderColor(BrandBlue).PaddingTop(8)
                    .Text(labels.Total).FontFamily(InvoiceFontSetup.Family).Bold().FontSize(11).FontColor(BrandBlue);
                table.Cell().ColumnSpan(2).BorderTop(1).BorderColor(BrandBlue);
                table.Cell().BorderTop(1).BorderColor(BrandBlue).PaddingTop(8).AlignRight()
                    .Text($"€{invoiceData.TotalAmount:F2}").FontFamily(InvoiceFontSetup.Family).Bold().FontSize(11).FontColor(BrandBlue);
            });
        }

        private static void ComposeInvoiceMainTable(
            IContainer container,
            InvoicePdfLabels labels,
            InvoiceData invoiceData,
            decimal totalExclVat,
            decimal vatAmount,
            DateTime dueDate)
        {
            container.Column(col =>
            {
                col.Item().Element(c => ComposeInvoiceMetaTable(c, labels, invoiceData, dueDate));
                col.Item().PaddingTop(24).Element(c =>
                    ComposeInvoiceSummaryTable(c, labels, invoiceData, totalExclVat, vatAmount));
            });
        }

        private static string BuildStreetLine(InvoiceData invoiceData)
        {
            if (string.IsNullOrWhiteSpace(invoiceData.Street))
                return string.Empty;

            var streetLine = invoiceData.Street;
            if (!string.IsNullOrWhiteSpace(invoiceData.HouseNumber))
                streetLine += $" {invoiceData.HouseNumber}";
            return streetLine;
        }

        private static string FormatSessionStationLine(SessionData session, string cardPrefix)
        {
            var title = string.IsNullOrWhiteSpace(session.ChargingStationName)
                ? $"#{session.StationId}"
                : session.ChargingStationName;
            var parts = new List<string> { title };
            if (!string.IsNullOrWhiteSpace(session.StationSerialNumber))
                parts.Add($"S/N {session.StationSerialNumber}");
            if (session.ChargeCardId != 0)
                parts.Add($"{cardPrefix} {session.ChargeCardId}");
            return string.Join("  ", parts);
        }

        private static void ComposePaymentSection(
            IContainer container,
            InvoicePdfLabels labels,
            InvoiceData invoiceData,
            byte[]? paymentQrCode,
            byte[]? sepaQrCode)
        {
            container.Background(PaymentBoxBackground).Padding(12).Column(col =>
            {
                col.Item().Text(labels.PaymentTitle)
                    .FontFamily(InvoiceFontSetup.Family).Bold().FontSize(18).FontColor(BrandBlue);

                if (invoiceData.UsePaymentRequestNotice)
                {
                    col.Item().PaddingTop(8).Text(labels.PaymentDue).FontFamily(InvoiceFontSetup.Family).FontSize(12);
                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text(labels.PaymentRequestNotice).FontFamily(InvoiceFontSetup.Family).FontSize(12);
                            left.Item().PaddingTop(10).Text(labels.QrHint)
                                .FontFamily(InvoiceFontSetup.Family).FontSize(9).FontColor(MutedGrey);
                        });

                        if (sepaQrCode != null)
                        {
                            row.ConstantItem(120).AlignRight().Column(qrCol =>
                            {
                                qrCol.Item().AlignCenter().Text(labels.ScanToPay).FontFamily(InvoiceFontSetup.Family).FontSize(9).Bold();
                                qrCol.Item().PaddingTop(4).Width(100).Height(100).Image(sepaQrCode);
                            });
                        }
                    });
                    col.Item().PaddingTop(12).Text(labels.PaymentContact)
                        .FontFamily(InvoiceFontSetup.Family).FontSize(12).FontColor(MutedGrey);
                    return;
                }

                if (invoiceData.IsPaid)
                {
                    col.Item().PaddingTop(12).Background(Colors.Green.Lighten4).Padding(10)
                        .Text(labels.PaymentPaid)
                        .FontFamily(InvoiceFontSetup.Family).Bold().FontColor(Colors.Green.Darken2);
                }
                else
                {
                    col.Item().PaddingTop(8).Text(labels.PaymentDue).FontFamily(InvoiceFontSetup.Family).FontSize(12);
                    if (!string.IsNullOrWhiteSpace(invoiceData.PaymentUrl))
                    {
                        col.Item().PaddingTop(10).Text(labels.PayOnline).FontFamily(InvoiceFontSetup.Family).Bold().FontSize(12);
                        col.Item().PaddingTop(6).Row(row =>
                        {
                            row.RelativeItem().Column(linkCol =>
                            {
                                linkCol.Item().Text(invoiceData.PaymentUrl)
                                    .FontFamily(InvoiceFontSetup.Family).FontColor(BrandBlue).FontSize(10);
                                linkCol.Item().PaddingTop(3).Text(labels.QrHint)
                                    .FontFamily(InvoiceFontSetup.Family).FontSize(9).FontColor(MutedGrey);
                            });

                            if (paymentQrCode != null)
                            {
                                row.ConstantItem(120).AlignRight().Column(qrCol =>
                                {
                                    qrCol.Item().AlignCenter().Text(labels.ScanToPay).FontFamily(InvoiceFontSetup.Family).FontSize(9).Bold();
                                    qrCol.Item().PaddingTop(4).Width(100).Height(100).Image(paymentQrCode);
                                });
                            }
                        });
                    }

                    // Also show SEPA bank-transfer QR as a fallback for regular invoices.
                    if (sepaQrCode != null)
                    {
                        col.Item().PaddingTop(12).Text("Of maak het bedrag over via bankoverschrijving:")
                            .FontFamily(InvoiceFontSetup.Family).Bold().FontSize(12);
                        col.Item().PaddingTop(6).Row(row =>
                        {
                            row.RelativeItem().Column(left =>
                            {
                                left.Item().Text(labels.PaymentRequestNotice).FontFamily(InvoiceFontSetup.Family).FontSize(12);
                                left.Item().PaddingTop(10).Text(labels.QrHint)
                                    .FontFamily(InvoiceFontSetup.Family).FontSize(9).FontColor(MutedGrey);
                            });

                            row.ConstantItem(120).AlignRight().Column(qrCol =>
                            {
                                qrCol.Item().AlignCenter().Text(labels.ScanToPay).FontFamily(InvoiceFontSetup.Family).FontSize(9).Bold();
                                qrCol.Item().PaddingTop(4).Width(100).Height(100).Image(sepaQrCode);
                            });
                        });
                    }
                }

                col.Item().PaddingTop(12).Text(labels.PaymentContact)
                    .FontFamily(InvoiceFontSetup.Family).FontSize(12).FontColor(MutedGrey);
                col.Item().PaddingTop(6).Text(text =>
                {
                    text.Span($"{labels.PortalPrefix} ").FontFamily(InvoiceFontSetup.Family).FontSize(10).FontColor(MutedGrey);
                    text.Hyperlink(CustomerPortalUrl, CustomerPortalUrl.TrimEnd('/'))
                        .FontFamily(InvoiceFontSetup.Family).FontSize(10).FontColor(BrandBlue);
                });
            });
        }

        private static void ComposePageOneFooter(
            IContainer container,
            InvoicePdfLabels labels,
            InvoiceData invoiceData,
            byte[]? paymentQrCode,
            byte[]? sepaQrCode)
        {
            container.Column(footer =>
            {
                footer.Item().Element(c => ComposePaymentSection(c, labels, invoiceData, paymentQrCode, sepaQrCode));
                footer.Item().PaddingTop(10).Element(ComposeSupplierFooter);
            });
        }

        private static void ComposeSessionsTable(
            IContainer container,
            InvoicePdfLabels labels,
            InvoiceData invoiceData)
        {
            container.Table(table =>
            {
                // Content-driven widths: tight date/number cols, Laadpaal gets all remaining space.
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(66);  // dd/MM HH:mm
                    columns.ConstantColumn(66);
                    columns.RelativeColumn(1);
                    columns.ConstantColumn(36);  // kWh e.g. 999.99
                    columns.ConstantColumn(106); // €9999.99 + header
                });

                table.Header(header =>
                {
                    void HeaderCell(IContainer cell, string text, bool rightAlign = false)
                    {
                        var box = cell.Background(BrandBlue).PaddingVertical(5).PaddingHorizontal(3).AlignMiddle();
                        if (rightAlign)
                            box.AlignRight();
                        box.Text(text)
                            .FontFamily(InvoiceFontSetup.Family).Bold().FontSize(11).FontColor(Colors.White);
                    }

                    HeaderCell(header.Cell(), labels.ColStart);
                    HeaderCell(header.Cell(), labels.ColEnd);
                    HeaderCell(header.Cell(), labels.ColStation);
                    HeaderCell(header.Cell(), labels.ColKwh, rightAlign: true);
                    HeaderCell(header.Cell(), labels.ColAmount, rightAlign: true);
                });

                foreach (var session in invoiceData.Sessions)
                {
                    table.Cell().PaddingVertical(4).PaddingHorizontal(3).AlignMiddle()
                        .Text($"{session.Start:dd/MM HH:mm}").FontFamily(InvoiceFontSetup.Family).FontSize(12);
                    table.Cell().PaddingVertical(4).PaddingHorizontal(3).AlignMiddle()
                        .Text(session.End.HasValue ? $"{session.End.Value:dd/MM HH:mm}" : labels.SessionOngoing)
                        .FontFamily(InvoiceFontSetup.Family).FontSize(12);
                    table.Cell().PaddingVertical(4).PaddingHorizontal(3).AlignMiddle()
                        .Text(FormatSessionStationLine(session, labels.SessionCardPrefix))
                        .FontFamily(InvoiceFontSetup.Family).FontSize(12);
                    table.Cell().PaddingVertical(4).PaddingHorizontal(3).AlignMiddle().AlignRight()
                        .Text($"{session.ChargedKwh:F2}").FontFamily(InvoiceFontSetup.Family).FontSize(12);
                    table.Cell().PaddingVertical(4).PaddingHorizontal(3).AlignMiddle().AlignRight()
                        .Text($"€{session.Amount:F2}").FontFamily(InvoiceFontSetup.Family).FontSize(12);
                }
            });
        }

        public byte[] GenerateInvoicePdf(InvoiceData invoiceData)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            InvoiceFontSetup.EnsureRegistered();

            var labels = LabelsFor(invoiceData.UsePaymentRequestNotice);
            var (logoPath, sunCornerPath) = ResolveBrandAssets();
            var (totalExclVat, vatAmount) = InvoiceVat.SplitInclusive(invoiceData.TotalAmount);
            var paymentQrCode = TryGeneratePaymentQrCode(invoiceData.PaymentUrl);
            var sepaQrCode = TryGenerateSepaTransferQrCode(invoiceData);
            var dueDate = invoiceData.GeneratedAt.Date.AddDays(30);
            var sessionsSubtitle = $"{labels.SessionsTitle}  {invoiceData.InvoiceNumber}";

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginLeft(2.25f, Unit.Centimetre);
                    page.MarginRight(2.15f, Unit.Centimetre);
                    page.MarginTop(1.5f, Unit.Centimetre);
                    page.MarginBottom(2.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily(InvoiceFontSetup.Family).FontSize(11));

                    if (!string.IsNullOrEmpty(sunCornerPath))
                        page.Background().Element(c => ComposeSunCorner(c, sunCornerPath));

                    page.Header().Element(c => ComposeBrandHeader(c, logoPath));

                    page.Content().PaddingTop(8).Column(column =>
                    {
                        column.Item().AlignCenter().Text(labels.Title)
                            .FontFamily(InvoiceFontSetup.Family).Bold().FontSize(22).FontColor(BrandBlue);

                        column.Item().PaddingTop(18).Element(c =>
                            ComposeInvoiceMainTable(c, labels, invoiceData, totalExclVat, vatAmount, dueDate));
                    });

                    page.Footer().Element(c =>
                        ComposePageOneFooter(c, labels, invoiceData, paymentQrCode, sepaQrCode));
                });

                if (invoiceData.Sessions.Any())
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.MarginLeft(2.25f, Unit.Centimetre);
                        page.MarginRight(2.15f, Unit.Centimetre);
                        page.MarginTop(1.5f, Unit.Centimetre);
                        page.MarginBottom(2.5f, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontFamily(InvoiceFontSetup.Family).FontSize(12));

                        if (!string.IsNullOrEmpty(sunCornerPath))
                            page.Background().Element(c => ComposeSunCorner(c, sunCornerPath));

                        page.Header().Element(c => ComposeBrandHeader(c, logoPath, sessionsSubtitle));

                        page.Content().PaddingTop(10).Element(c =>
                            ComposeSessionsTable(c, labels, invoiceData));

                        page.Footer().Element(ComposeSupplierFooter);
                    });
                }
            });

            return document.GeneratePdf();
        }
    }
}
