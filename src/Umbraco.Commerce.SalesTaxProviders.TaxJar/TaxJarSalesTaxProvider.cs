using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Taxjar;
using Umbraco.Commerce.Common.Logging;
using Umbraco.Commerce.Core.Api;
using Umbraco.Commerce.Core.Models;
using Umbraco.Commerce.Core.SalesTaxProviders;

namespace Umbraco.Commerce.SalesTaxProviders.TaxJar
{
    [SalesTaxProvider("taxjar")]
    public class TaxJarSalesTaxProvider(
        UmbracoCommerceContext ctx,
        ILogger<TaxJarSalesTaxProvider> logger)
        : SalesTaxProviderBase<TaxJarSettings>(ctx)
    {
        // RestSharp (used by the TaxJar SDK) waits 100 seconds by default, far longer than a checkout should hang
        private const int RequestTimeoutMilliseconds = 15000;

        public override async Task<SalesTaxCalculationResult> CalculateSalesTaxAsync(
            SalesTaxProviderContext<TaxJarSettings> context, CancellationToken cancellationToken = default)
        {
            var zeroAmount = Amount.ZeroValue(context.Order.CurrencyId);

            // Ensure we have a valid address to calculate tax
            if (string.IsNullOrWhiteSpace(context.FromAddress.CountryIsoCode)
                || string.IsNullOrWhiteSpace(context.FromAddress.ZipCode)
                || !context.FromAddress.CountryIsoCode.Equals("US", System.StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(context.ToAddress.CountryIsoCode)
                || string.IsNullOrWhiteSpace(context.ToAddress.ZipCode)
                || !context.ToAddress.CountryIsoCode.Equals("US", System.StringComparison.OrdinalIgnoreCase))
            {
                return new SalesTaxCalculationResult(zeroAmount);
            }

            // Ensure the order is in USD
            CurrencyReadOnly currency = await ctx.Services.CurrencyService.GetCurrencyAsync(context.Order.CurrencyId);
            if (currency.Code.Equals("USD", System.StringComparison.OrdinalIgnoreCase) == false)
            {
                return new SalesTaxCalculationResult(zeroAmount);
            }

            // Resolve reusable data
            StoreReadOnly store = await ctx.Services.StoreService.GetStoreAsync(context.Order.StoreId);
            IEnumerable<TaxClassReadOnly> taxClasses = (await ctx.Services.TaxService.GetTaxClassesAsync(context.Order.StoreId)).ToList();
            TaxClassReadOnly? storeDefaultTaxClass = store.DefaultTaxClassId.HasValue
                ? taxClasses.FirstOrDefault(x => x.Id == store.DefaultTaxClassId.Value)
                : null;
            TaxSource? taxSource = context.Order.ShippingInfo.CountryId.HasValue
                ? new TaxSource(context.Order.ShippingInfo.CountryId.Value, context.Order.ShippingInfo.RegionId)
                : context.Order.PaymentInfo.CountryId.HasValue
                    ? new TaxSource(context.Order.PaymentInfo.CountryId.Value, context.Order.PaymentInfo.RegionId)
                    : null;

            // Without a country the order's tax codes can't be resolved
            if (taxSource == null)
            {
                logger.Debug("Unable to calculate TaxJar sales tax as the order has no shipping or payment country");
                return new SalesTaxCalculationResult(zeroAmount);
            }

            // Create the TaxJar client
            var client = new TaxjarApi(context.Settings.TestMode ? context.Settings.SandboxToken : context.Settings.LiveToken, new
            {
                apiUrl = context.Settings.TestMode ? "https://api.sandbox.taxjar.com" : "https://api.taxjar.com",
                timeout = RequestTimeoutMilliseconds,
            });

            // Calculate tax
            try
            {
                TaxResponseAttributes? taxResponse = await client.TaxForOrderAsync(new Tax
                {
                    // From Address
                    FromStreet = context.FromAddress.AddressLine1,
                    FromCity = context.FromAddress.City,
                    FromState = context.FromAddress.Region,
                    FromCountry = context.FromAddress.CountryIsoCode,
                    FromZip = context.FromAddress.ZipCode,

                    // To Address
                    ToStreet = context.ToAddress.AddressLine1,
                    ToCity = context.ToAddress.City,
                    ToState = context.ToAddress.Region,
                    ToCountry = context.ToAddress.CountryIsoCode,
                    ToZip = context.ToAddress.ZipCode,

                    // Subtotal
                    Amount = context.OrderCalculation.SubtotalPrice.Value.WithoutTax,

                    // Shipping
                    Shipping = context.OrderCalculation.ShippingTotalPrice.Value.WithoutTax,

                    // Order Lines
                    LineItems = context.Order.OrderLines.Select(x =>
                    {
                        OrderLineCalculation? orderLineCalc = context.OrderCalculation.OrderLines[x.Id];

                        // An order line's tax class that isn't found falls back to the store default
                        TaxClassReadOnly? taxClass = (x.TaxClassId.HasValue
                            ? taxClasses.FirstOrDefault(y => y.Id == x.TaxClassId.Value)
                            : null) ?? storeDefaultTaxClass;

                        return BuildLineItem(
                            x.Sku,
                            x.Quantity,
                            taxClass?.GetTaxCode(taxSource),
                            // TotalPrice.TotalAdjustment already includes the unit price adjustments (x quantity),
                            // so pair it with the unit price before adjustments to land on the line's total
                            orderLineCalc.UnitPrice.WithoutAdjustments.WithoutTax,
                            orderLineCalc.TotalPrice.TotalAdjustment.WithoutTax);

                    }).ToList()
                }).ConfigureAwait(false);

                // Format Result
                var result = new SalesTaxCalculationResult(new Amount(taxResponse.AmountToCollect, context.Order.CurrencyId))
                {
                    Jurisdictions = new Dictionary<string, string>
                    {
                        { "city", taxResponse.Jurisdictions.City },
                        { "county", taxResponse.Jurisdictions.County },
                        { "state", taxResponse.Jurisdictions.State },
                        { "country", taxResponse.Jurisdictions.Country }
                    },
                    Breakdown = new []
                    {
                        new SalesTaxBreakdown(new Amount(taxResponse.Breakdown.CityTaxCollectable, context.Order.CurrencyId), "city"),
                        new SalesTaxBreakdown(new Amount(taxResponse.Breakdown.CountyTaxCollectable, context.Order.CurrencyId), "county"),
                        new SalesTaxBreakdown(new Amount(taxResponse.Breakdown.StateTaxCollectable, context.Order.CurrencyId), "state"),
                        new SalesTaxBreakdown(new Amount(taxResponse.Breakdown.CountryTaxCollectable, context.Order.CurrencyId), "country"),
                        new SalesTaxBreakdown(new Amount(taxResponse.Breakdown.SpecialDistrictTaxCollectable, context.Order.CurrencyId), "special_district"),
                    }
                };

                return result;

            }
            catch (Exception e) when (IsTimeout(e))
            {
                logger.Warn(e, "TaxJar didn't respond within {TimeoutSeconds} seconds; no sales tax was calculated", RequestTimeoutMilliseconds / 1000);

                return new SalesTaxCalculationResult(zeroAmount);
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to calculate sales tax using TaxJar API");

                return new SalesTaxCalculationResult(zeroAmount);
            }
        }

        /// <summary>
        /// The TaxJar SDK wraps a timed-out request in a plain <see cref="Exception"/> whose inner exception is
        /// the cancellation. The SDK is never given a cancellation token, so any cancellation is the timeout.
        /// </summary>
        private static bool IsTimeout(Exception exception)
        {
            for (Exception? e = exception; e != null; e = e.InnerException)
            {
                if (e is OperationCanceledException or TimeoutException)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds a TaxJar line item so that quantity x unit price - discount is the order line's total.
        /// TaxJar only accepts whole quantities, so a fractional quantity (1.5 kg, say) is sent as one unit
        /// priced at the line's total, which keeps the taxable amount exact.
        /// TaxJar's discount is a positive amount off the line, while Commerce adjustments are negative for
        /// discounts and positive for surcharges, so <c>totalAdjustment</c> (the order line's total price adjustment)
        /// is negated into the discount, and a surcharge is added to the line's price instead.
        /// </summary>
        private static TaxLineItem BuildLineItem(
            string sku,
            decimal quantity,
            string? productTaxCode,
            decimal unitPrice,
            decimal totalAdjustment)
        {
            var discount = totalAdjustment < 0 ? -totalAdjustment : 0m;
            var surcharge = totalAdjustment > 0 ? totalAdjustment : 0m;

            if (quantity == decimal.Truncate(quantity) && surcharge == 0)
            {
                return new TaxLineItem
                {
                    Id = sku,
                    Quantity = (int)quantity,
                    ProductTaxCode = productTaxCode,
                    UnitPrice = unitPrice,
                    Discount = discount
                };
            }

            return new TaxLineItem
            {
                Id = sku,
                Quantity = 1,
                ProductTaxCode = productTaxCode,
                UnitPrice = (unitPrice * quantity) + surcharge,
                Discount = discount
            };
        }
    }
}
