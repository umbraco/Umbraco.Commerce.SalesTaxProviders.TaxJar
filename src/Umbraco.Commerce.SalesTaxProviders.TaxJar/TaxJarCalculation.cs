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

        /// <summary>
        /// Picks the order line's tax class when it exists in the store, otherwise the store's default tax class.
        /// Returns <c>null</c> when neither exists.
        /// </summary>
        public static Guid? ResolveTaxClassId(Guid? orderLineTaxClassId, ICollection<Guid> storeTaxClassIds, Guid? storeDefaultTaxClassId)
        {
            if (orderLineTaxClassId.HasValue && storeTaxClassIds.Contains(orderLineTaxClassId.Value))
            {
                return orderLineTaxClassId;
            }

            if (storeDefaultTaxClassId.HasValue && storeTaxClassIds.Contains(storeDefaultTaxClassId.Value))
            {
                return storeDefaultTaxClassId;
            }

            return null;
        }

        /// <summary>
        /// Builds a TaxJar line item so that quantity x unit price - discount is the order line's total.
        /// TaxJar only accepts whole quantities, so a fractional quantity (1.5 kg, say) is sent as one unit
        /// priced at the line's total, which keeps the taxable amount exact.
        /// TaxJar's discount is a positive amount off the line, while Commerce adjustments are negative for
        /// discounts and positive for surcharges, so <c>totalAdjustment</c> (the order line's total price adjustment)
        /// is negated into the discount, and a surcharge is added to the line's price instead.
        /// </summary>
        public static TaxLineItem BuildLineItem(
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
                return new()
                {
                    Id = sku,
                    Quantity = (int)quantity,
                    ProductTaxCode = productTaxCode,
                    UnitPrice = unitPrice,
                    Discount = discount,
                };
            }

            return new()
            {
                Id = sku,
                Quantity = 1,
                ProductTaxCode = productTaxCode,
                UnitPrice = (unitPrice * quantity) + surcharge,
                Discount = discount,
            };
        }

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
                    new SalesTaxBreakdown(new Amount(taxResponse.Breakdown.SpecialDistrictTaxCollectable, currencyId), "special_district"),
                },
            };
    }
}
