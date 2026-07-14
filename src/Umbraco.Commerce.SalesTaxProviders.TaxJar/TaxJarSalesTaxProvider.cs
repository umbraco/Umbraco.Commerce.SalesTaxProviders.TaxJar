using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Umbraco.Commerce.Common.Logging;
using Umbraco.Commerce.Core.Api;
using Umbraco.Commerce.Core.Models;
using Umbraco.Commerce.Core.SalesTaxProviders;

namespace Umbraco.Commerce.SalesTaxProviders.TaxJar
{
    [SalesTaxProvider("taxjar")]
    public class TaxJarSalesTaxProvider(
        UmbracoCommerceContext ctx,
        ILogger<TaxJarSalesTaxProvider> logger,
        IHttpClientFactory httpClientFactory)
        : SalesTaxProviderBase<TaxJarSettings>(ctx)
    {
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
            TaxSource taxSource = context.Order.ShippingInfo.CountryId.HasValue
                ? new TaxSource(context.Order.ShippingInfo.CountryId.Value, context.Order.ShippingInfo.RegionId)
                : new TaxSource(context.Order.PaymentInfo.CountryId!.Value, context.Order.PaymentInfo.RegionId);

            var token = context.Settings.TestMode ? context.Settings.SandboxToken : context.Settings.LiveToken;
            var apiUrl = context.Settings.TestMode ? "https://api.sandbox.taxjar.com" : "https://api.taxjar.com";

            HttpClient client = httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(apiUrl);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            // Calculate tax
            try
            {
                var request = new TaxJarRequest
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

                        TaxClassReadOnly? taxClass = x.TaxClassId.HasValue
                            ? taxClasses?.FirstOrDefault(y => y.Id == x.TaxClassId.Value)
                            : storeDefaultTaxClass;

                        return new TaxJarLineItem
                        {
                            Id = x.Sku,
                            Quantity = (int)x.Quantity,
                            ProductTaxCode = taxClass?.GetTaxCode(taxSource),
                            UnitPrice = orderLineCalc.UnitPrice.Value.WithoutTax,
                            Discount = orderLineCalc.TotalPrice.TotalAdjustment.WithoutTax
                        };

                    }).ToList()
                };

                HttpResponseMessage response = await client.PostAsJsonAsync("/v2/taxes", request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                TaxJarResponse? taxResponse = await response.Content.ReadFromJsonAsync<TaxJarResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);

                if (taxResponse?.Tax == null)
                {
                    logger.Error("TaxJar API returned invalid response");
                    return new SalesTaxCalculationResult(zeroAmount);
                }

                // Format Result
                var result = new SalesTaxCalculationResult(new Amount(taxResponse.Tax.AmountToCollect, context.Order.CurrencyId))
                {
                    Jurisdictions = new Dictionary<string, string>
                    {
                        { "city", taxResponse.Tax.Jurisdictions?.City ?? string.Empty },
                        { "county", taxResponse.Tax.Jurisdictions?.County ?? string.Empty },
                        { "state", taxResponse.Tax.Jurisdictions?.State ?? string.Empty },
                        { "country", taxResponse.Tax.Jurisdictions?.Country ?? string.Empty },
                    },
                    Breakdown = new []
                    {
                        new SalesTaxBreakdown(new Amount(taxResponse.Tax.Breakdown?.CityTaxCollectable ?? 0, context.Order.CurrencyId), "city"),
                        new SalesTaxBreakdown(new Amount(taxResponse.Tax.Breakdown?.CountyTaxCollectable ?? 0, context.Order.CurrencyId), "county"),
                        new SalesTaxBreakdown(new Amount(taxResponse.Tax.Breakdown?.StateTaxCollectable ?? 0, context.Order.CurrencyId), "state"),
                        new SalesTaxBreakdown(new Amount(taxResponse.Tax.Breakdown?.CountryTaxCollectable ?? 0, context.Order.CurrencyId), "country"),
                    },
                };

                return result;

            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to calculate sales tax using TaxJar API");

                return new SalesTaxCalculationResult(zeroAmount);
            }
        }
    }
}
