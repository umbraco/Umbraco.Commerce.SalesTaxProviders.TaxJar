using System;
using System.Collections.Generic;
using Taxjar;
using Umbraco.Commerce.Core.Models;
using Umbraco.Commerce.Core.SalesTaxProviders;
using Address = Umbraco.Commerce.Core.Models.Address;

namespace Umbraco.Commerce.SalesTaxProviders.TaxJar
{
    /// <summary>
    /// The parts of the TaxJar calculation that need neither the Commerce services nor the TaxJar API.
    /// </summary>
    internal static class TaxJarCalculation
    {
        /// <summary>TaxJar sales tax only applies when both addresses are in the US and have a zip code.</summary>
        public static bool IsSupportedAddress(Address address)
            => !string.IsNullOrWhiteSpace(address.CountryIsoCode)
                && !string.IsNullOrWhiteSpace(address.ZipCode)
                && address.CountryIsoCode.Equals("US", StringComparison.OrdinalIgnoreCase);

        public static bool IsUsd(string currencyCode)
            => currencyCode.Equals("USD", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Uses the shipping country and region when set, otherwise the payment ones.
        /// Returns <c>null</c> when the order has neither a shipping nor a payment country.
        /// </summary>
        public static TaxSource? ResolveTaxSource(
            Guid? shippingCountryId,
            Guid? shippingRegionId,
            Guid? paymentCountryId,
            Guid? paymentRegionId)
        {
            if (shippingCountryId.HasValue)
            {
                return new TaxSource(shippingCountryId.Value, shippingRegionId);
            }

            if (paymentCountryId.HasValue)
            {
                return new TaxSource(paymentCountryId.Value, paymentRegionId);
            }

            return null;
        }

        public static TaxLineItem BuildLineItem(
            string sku,
            decimal quantity,
            string? productTaxCode,
            decimal unitPrice,
            decimal discount)
            => new()
            {
                Id = sku,
                Quantity = (int)quantity,
                ProductTaxCode = productTaxCode,
                UnitPrice = unitPrice,
                Discount = discount,
            };

        public static Tax BuildTaxRequest(
            Address fromAddress,
            Address toAddress,
            decimal subtotal,
            decimal shipping,
            List<TaxLineItem> lineItems)
            => new()
            {
                // From Address
                FromStreet = fromAddress.AddressLine1,
                FromCity = fromAddress.City,
                FromState = fromAddress.Region,
                FromCountry = fromAddress.CountryIsoCode,
                FromZip = fromAddress.ZipCode,

                // To Address
                ToStreet = toAddress.AddressLine1,
                ToCity = toAddress.City,
                ToState = toAddress.Region,
                ToCountry = toAddress.CountryIsoCode,
                ToZip = toAddress.ZipCode,

                // Subtotal
                Amount = subtotal,

                // Shipping
                Shipping = shipping,

                // Order Lines
                LineItems = lineItems,
            };

        public static SalesTaxCalculationResult MapResponse(TaxResponseAttributes taxResponse, Guid currencyId)
            => new(new Amount(taxResponse.AmountToCollect, currencyId))
            {
                Jurisdictions = new Dictionary<string, string>
                {
                    { "city", taxResponse.Jurisdictions.City },
                    { "county", taxResponse.Jurisdictions.County },
                    { "state", taxResponse.Jurisdictions.State },
                    { "country", taxResponse.Jurisdictions.Country },
                },
                Breakdown = new[]
                {
                    new SalesTaxBreakdown(new Amount(taxResponse.Breakdown.CityTaxCollectable, currencyId), "city"),
                    new SalesTaxBreakdown(new Amount(taxResponse.Breakdown.CountyTaxCollectable, currencyId), "county"),
                    new SalesTaxBreakdown(new Amount(taxResponse.Breakdown.StateTaxCollectable, currencyId), "state"),
                    new SalesTaxBreakdown(new Amount(taxResponse.Breakdown.CountryTaxCollectable, currencyId), "country"),
                },
            };
    }
}
