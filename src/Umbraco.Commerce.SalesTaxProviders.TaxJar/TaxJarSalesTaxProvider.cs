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
        internal const int RequestTimeoutMilliseconds = 15000;

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
            Dictionary<Guid, TaxClassReadOnly> taxClasses = (await ctx.Services.TaxService.GetTaxClassesAsync(context.Order.StoreId))
                .ToDictionary(x => x.Id);
            TaxSource? taxSource = TaxJarCalculation.ResolveTaxSource(
                context.Order.ShippingInfo.CountryId,
                context.Order.ShippingInfo.RegionId,
                context.Order.PaymentInfo.CountryId,
                context.Order.PaymentInfo.RegionId);

            // Without a country the order's tax codes can't be resolved
            if (taxSource == null)
            {
                logger.Debug("Unable to calculate TaxJar sales tax as the order has no shipping or payment country");
                return new SalesTaxCalculationResult(zeroAmount);
            }

            // Create the TaxJar client
            var client = CreateClient(context.Settings);

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

                        Guid? taxClassId = TaxJarCalculation.ResolveTaxClassId(x.TaxClassId, taxClasses.Keys, store.DefaultTaxClassId);
                        TaxClassReadOnly? taxClass = taxClassId.HasValue ? taxClasses[taxClassId.Value] : null;

                        return TaxJarCalculation.BuildLineItem(
                            x.Sku,
                            x.Quantity,
                            taxClass?.GetTaxCode(taxSource),
                            // TotalPrice.TotalAdjustment already includes the unit price adjustments (x quantity),
                            // so pair it with the unit price before adjustments to land on the line's total
                            orderLineCalc.UnitPrice.WithoutAdjustments.WithoutTax,
                            orderLineCalc.TotalPrice.TotalAdjustment.WithoutTax);
                    }).ToList())).ConfigureAwait(false);

                // Format Result
                return TaxJarCalculation.MapResponse(taxResponse, context.Order.CurrencyId);
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

        internal static TaxjarApi CreateClient(TaxJarSettings settings)
            => new TaxjarApi(settings.TestMode ? settings.SandboxToken : settings.LiveToken, new
            {
                apiUrl = settings.TestMode ? "https://api.sandbox.taxjar.com" : "https://api.taxjar.com",
                timeout = RequestTimeoutMilliseconds,
            });

        /// <summary>
        /// The TaxJar SDK wraps a timed-out request in a plain <see cref="Exception"/> whose inner exception is
        /// the cancellation. The SDK is never given a cancellation token, so any cancellation is the timeout.
        /// </summary>
        internal static bool IsTimeout(Exception exception)
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
    }
}
