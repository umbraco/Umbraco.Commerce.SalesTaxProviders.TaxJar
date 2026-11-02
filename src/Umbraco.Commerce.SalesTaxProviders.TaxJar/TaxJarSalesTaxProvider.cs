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
        public override async Task<SalesTaxCalculationResult> CalculateSalesTaxAsync(
            SalesTaxProviderContext<TaxJarSettings> context, CancellationToken cancellationToken = default)
        {
            var zeroAmount = Amount.ZeroValue(context.Order.CurrencyId);

            // Ensure we have a valid address to calculate tax
            if (!TaxJarCalculation.IsSupportedAddress(context.FromAddress)
                || !TaxJarCalculation.IsSupportedAddress(context.ToAddress))
            {
                return new SalesTaxCalculationResult(zeroAmount);
            }

            // Ensure the order is in USD
            CurrencyReadOnly currency = await ctx.Services.CurrencyService.GetCurrencyAsync(context.Order.CurrencyId);
            if (!TaxJarCalculation.IsUsd(currency.Code))
            {
                return new SalesTaxCalculationResult(zeroAmount);
            }

            // Resolve reusable data
            StoreReadOnly store = await ctx.Services.StoreService.GetStoreAsync(context.Order.StoreId);
            IEnumerable<TaxClassReadOnly> taxClasses = (await ctx.Services.TaxService.GetTaxClassesAsync(context.Order.StoreId)).ToList();
            TaxClassReadOnly? storeDefaultTaxClass = store.DefaultTaxClassId.HasValue
                ? taxClasses.FirstOrDefault(x => x.Id == store.DefaultTaxClassId.Value)
                : null;
            TaxSource taxSource = TaxJarCalculation.ResolveTaxSource(
                context.Order.ShippingInfo.CountryId,
                context.Order.ShippingInfo.RegionId,
                context.Order.PaymentInfo.CountryId,
                context.Order.PaymentInfo.RegionId);

            // Create the TaxJar client
            var client = new TaxjarApi(context.Settings.TestMode ? context.Settings.SandboxToken : context.Settings.LiveToken, new
            {
                apiUrl = context.Settings.TestMode ? "https://api.sandbox.taxjar.com" : "https://api.taxjar.com"
            });

            // Calculate tax
            try
            {
                TaxResponseAttributes? taxResponse = await client.TaxForOrderAsync(TaxJarCalculation.BuildTaxRequest(
                    context.FromAddress,
                    context.ToAddress,
                    context.OrderCalculation.SubtotalPrice.Value.WithoutTax,
                    context.OrderCalculation.ShippingTotalPrice.Value.WithoutTax,
                    context.Order.OrderLines.Select(x =>
                    {
                        OrderLineCalculation? orderLineCalc = context.OrderCalculation.OrderLines[x.Id];

                        TaxClassReadOnly? taxClass = x.TaxClassId.HasValue
                            ? taxClasses?.FirstOrDefault(y => y.Id == x.TaxClassId.Value)
                            : storeDefaultTaxClass;

                        return TaxJarCalculation.BuildLineItem(
                            x.Sku,
                            x.Quantity,
                            taxClass?.GetTaxCode(taxSource),
                            orderLineCalc.UnitPrice.Value.WithoutTax,
                            orderLineCalc.TotalPrice.TotalAdjustment.WithoutTax);
                    }).ToList())).ConfigureAwait(false);

                // Format Result
                return TaxJarCalculation.MapResponse(taxResponse, context.Order.CurrencyId);
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to calculate sales tax using TaxJar API");

                return new SalesTaxCalculationResult(zeroAmount);
            }
        }
    }
}
