using FluentAssertions;
using Newtonsoft.Json;
using Taxjar;
using Umbraco.Commerce.Core.SalesTaxProviders;

namespace Umbraco.Commerce.SalesTaxProviders.TaxJar.UnitTests
{
    public class TaxJarResponseMappingTests
    {
        // A response in the shape of TaxJar's POST /v2/taxes documentation example
        internal const string TaxesResponseJson = """
            {
              "tax": {
                "order_total_amount": 16.5,
                "shipping": 1.5,
                "taxable_amount": 15.0,
                "amount_to_collect": 1.35,
                "rate": 0.09,
                "has_nexus": true,
                "freight_taxable": false,
                "tax_source": "destination",
                "jurisdictions": {
                  "country": "US",
                  "state": "CA",
                  "county": "LOS ANGELES COUNTY",
                  "city": "LOS ANGELES"
                },
                "breakdown": {
                  "taxable_amount": 15.0,
                  "tax_collectable": 1.35,
                  "combined_tax_rate": 0.09,
                  "state_taxable_amount": 15.0,
                  "state_tax_rate": 0.0625,
                  "state_tax_collectable": 0.94,
                  "county_taxable_amount": 15.0,
                  "county_tax_rate": 0.01,
                  "county_tax_collectable": 0.15,
                  "city_taxable_amount": 0.0,
                  "city_tax_rate": 0.0,
                  "city_tax_collectable": 0.0,
                  "special_district_taxable_amount": 15.0,
                  "special_tax_rate": 0.0175,
                  "special_district_tax_collectable": 0.26,
                  "line_items": [
                    {
                      "id": "1",
                      "taxable_amount": 15.0,
                      "tax_collectable": 1.35,
                      "combined_tax_rate": 0.09
                    }
                  ]
                }
              }
            }
            """;

        private readonly Guid _currencyId = Guid.NewGuid();

        [Fact]
        public void MapResponse_Should_Use_Amount_To_Collect_As_The_Tax_Amount()
        {
            // preparation
            TaxResponseAttributes response = Deserialize();

            // execution
            SalesTaxCalculationResult result = TaxJarCalculation.MapResponse(response, _currencyId);

            // asserts
            result.Amount.Value.Should().Be(1.35m);
            result.Amount.CurrencyId.Should().Be(_currencyId);
        }

        [Fact]
        public void MapResponse_Should_Map_The_Jurisdictions()
        {
            // preparation
            TaxResponseAttributes response = Deserialize();

            // execution
            SalesTaxCalculationResult result = TaxJarCalculation.MapResponse(response, _currencyId);

            // asserts
            result.Jurisdictions.Should().BeEquivalentTo(new Dictionary<string, string>
            {
                { "city", "LOS ANGELES" },
                { "county", "LOS ANGELES COUNTY" },
                { "state", "CA" },
                { "country", "US" },
            });
        }

        [Fact]
        public void MapResponse_Should_Create_Four_Breakdown_Rows_In_City_County_State_Country_Order()
        {
            // preparation
            TaxResponseAttributes response = Deserialize();

            // execution
            SalesTaxCalculationResult result = TaxJarCalculation.MapResponse(response, _currencyId);

            // asserts
            result.Breakdown.Select(x => x.Jurisdiction).Should().Equal("city", "county", "state", "country");
        }

        [Fact]
        public void MapResponse_Should_Map_The_Collectable_Tax_Of_Each_Breakdown_Row()
        {
            // preparation
            TaxResponseAttributes response = Deserialize();

            // execution
            SalesTaxCalculationResult result = TaxJarCalculation.MapResponse(response, _currencyId);

            // asserts
            result.Breakdown.ToDictionary(x => x.Jurisdiction, x => x.Amount.Value).Should().BeEquivalentTo(
                new Dictionary<string, decimal>
                {
                    { "city", 0m },
                    { "county", 0.15m },
                    { "state", 0.94m },
                    { "country", 0m },
                });
            result.Breakdown.Should().OnlyContain(x => x.Amount.CurrencyId == _currencyId);
        }

        internal static TaxResponseAttributes Deserialize()
            => JsonConvert.DeserializeObject<TaxResponse>(TaxesResponseJson)!.Tax;
    }
}
