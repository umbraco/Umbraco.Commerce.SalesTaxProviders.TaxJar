using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Umbraco.Commerce.SalesTaxProviders.TaxJar
{
    internal class TaxJarRequest
    {
        [JsonPropertyName("from_country")]
        public string? FromCountry { get; set; }

        [JsonPropertyName("from_zip")]
        public string? FromZip { get; set; }

        [JsonPropertyName("from_state")]
        public string? FromState { get; set; }

        [JsonPropertyName("from_city")]
        public string? FromCity { get; set; }

        [JsonPropertyName("from_street")]
        public string? FromStreet { get; set; }

        [JsonPropertyName("to_country")]
        public string? ToCountry { get; set; }

        [JsonPropertyName("to_zip")]
        public string? ToZip { get; set; }

        [JsonPropertyName("to_state")]
        public string? ToState { get; set; }

        [JsonPropertyName("to_city")]
        public string? ToCity { get; set; }

        [JsonPropertyName("to_street")]
        public string? ToStreet { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("shipping")]
        public decimal Shipping { get; set; }

        [JsonPropertyName("line_items")]
        public List<TaxJarLineItem>? LineItems { get; set; }
    }

    internal class TaxJarLineItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("product_tax_code")]
        public string? ProductTaxCode { get; set; }

        [JsonPropertyName("unit_price")]
        public decimal UnitPrice { get; set; }

        [JsonPropertyName("discount")]
        public decimal Discount { get; set; }
    }

    internal class TaxJarResponse
    {
        [JsonPropertyName("tax")]
        public TaxJarTaxData? Tax { get; set; }
    }

    internal class TaxJarTaxData
    {
        [JsonPropertyName("amount_to_collect")]
        public decimal AmountToCollect { get; set; }

        [JsonPropertyName("jurisdictions")]
        public TaxJarJurisdictions? Jurisdictions { get; set; }

        [JsonPropertyName("breakdown")]
        public TaxJarBreakdown? Breakdown { get; set; }
    }

    internal class TaxJarJurisdictions
    {
        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("county")]
        public string? County { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }
    }

    internal class TaxJarBreakdown
    {
        [JsonPropertyName("country_taxable_amount")]
        public decimal CountryTaxableAmount { get; set; }

        [JsonPropertyName("country_tax_collectable")]
        public decimal CountryTaxCollectable { get; set; }

        [JsonPropertyName("state_taxable_amount")]
        public decimal StateTaxableAmount { get; set; }

        [JsonPropertyName("state_tax_collectable")]
        public decimal StateTaxCollectable { get; set; }

        [JsonPropertyName("county_taxable_amount")]
        public decimal CountyTaxableAmount { get; set; }

        [JsonPropertyName("county_tax_collectable")]
        public decimal CountyTaxCollectable { get; set; }

        [JsonPropertyName("city_taxable_amount")]
        public decimal CityTaxableAmount { get; set; }

        [JsonPropertyName("city_tax_collectable")]
        public decimal CityTaxCollectable { get; set; }
    }
}
