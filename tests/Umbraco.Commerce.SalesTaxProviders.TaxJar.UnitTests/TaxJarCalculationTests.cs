using FluentAssertions;
using Taxjar;
using Umbraco.Commerce.Core.Models;
using Address = Umbraco.Commerce.Core.Models.Address;

namespace Umbraco.Commerce.SalesTaxProviders.TaxJar.UnitTests
{
    public class TaxJarCalculationTests
    {
        [Theory]
        [InlineData("US", "90210", true)]
        [InlineData("us", "90210", true)]
        [InlineData("DK", "8000", false)]
        [InlineData("US", "", false)]
        [InlineData("US", "  ", false)]
        [InlineData("US", null, false)]
        [InlineData("", "90210", false)]
        [InlineData(null, "90210", false)]
        public void IsSupportedAddress_Should_Require_A_US_Country_And_A_Zip(string? country, string? zip, bool expected)
        {
            // preparation
            Address address = new("1 Main St", "Beverly Hills", country!, zip!);

            // execution
            bool supported = TaxJarCalculation.IsSupportedAddress(address);

            // asserts
            supported.Should().Be(expected);
        }

        [Theory]
        [InlineData("USD", true)]
        [InlineData("usd", true)]
        [InlineData("EUR", false)]
        [InlineData("DKK", false)]
        public void IsUsd_Should_Only_Accept_USD(string code, bool expected)
        {
            // execution
            bool isUsd = TaxJarCalculation.IsUsd(code);

            // asserts
            isUsd.Should().Be(expected);
        }

        [Fact]
        public void ResolveTaxSource_Should_Use_The_Shipping_Country_And_Region_When_Set()
        {
            // preparation
            Guid shippingCountry = Guid.NewGuid();
            Guid shippingRegion = Guid.NewGuid();

            // execution
            TaxSource source = TaxJarCalculation.ResolveTaxSource(shippingCountry, shippingRegion, Guid.NewGuid(), Guid.NewGuid())!;

            // asserts
            source.CountryId.Should().Be(shippingCountry);
            source.RegionId.Should().Be(shippingRegion);
        }

        [Fact]
        public void ResolveTaxSource_Should_Use_The_Shipping_Country_Without_A_Region_When_Only_The_Country_Is_Set()
        {
            // preparation
            Guid shippingCountry = Guid.NewGuid();

            // execution
            TaxSource source = TaxJarCalculation.ResolveTaxSource(shippingCountry, null, Guid.NewGuid(), Guid.NewGuid())!;

            // asserts
            source.CountryId.Should().Be(shippingCountry);
            source.RegionId.Should().BeNull();
        }

        [Fact]
        public void ResolveTaxSource_Should_Fall_Back_To_The_Payment_Country_And_Region_When_There_Is_No_Shipping_Country()
        {
            // preparation
            Guid paymentCountry = Guid.NewGuid();
            Guid paymentRegion = Guid.NewGuid();

            // execution
            TaxSource source = TaxJarCalculation.ResolveTaxSource(null, null, paymentCountry, paymentRegion)!;

            // asserts
            source.CountryId.Should().Be(paymentCountry);
            source.RegionId.Should().Be(paymentRegion);
        }

        [Fact]
        public void ResolveTaxSource_Should_Return_Null_When_There_Is_No_Shipping_Or_Payment_Country()
        {
            // execution
            TaxSource? source = TaxJarCalculation.ResolveTaxSource(null, Guid.NewGuid(), null, Guid.NewGuid());

            // asserts
            source.Should().BeNull();
        }

        [Fact]
        public void ResolveTaxClassId_Should_Use_The_Order_Line_Tax_Class_When_It_Exists()
        {
            // preparation
            Guid lineClass = Guid.NewGuid();
            Guid defaultClass = Guid.NewGuid();

            // execution
            Guid? result = TaxJarCalculation.ResolveTaxClassId(lineClass, [lineClass, defaultClass], defaultClass);

            // asserts
            result.Should().Be(lineClass);
        }

        [Fact]
        public void ResolveTaxClassId_Should_Use_The_Store_Default_When_The_Order_Line_Has_No_Tax_Class()
        {
            // preparation
            Guid defaultClass = Guid.NewGuid();

            // execution
            Guid? result = TaxJarCalculation.ResolveTaxClassId(null, [defaultClass], defaultClass);

            // asserts
            result.Should().Be(defaultClass);
        }

        [Fact]
        public void ResolveTaxClassId_Should_Use_The_Store_Default_When_The_Order_Line_Tax_Class_Is_Not_Found()
        {
            // preparation
            Guid defaultClass = Guid.NewGuid();

            // execution
            Guid? result = TaxJarCalculation.ResolveTaxClassId(Guid.NewGuid(), [defaultClass], defaultClass);

            // asserts
            result.Should().Be(defaultClass);
        }

        [Fact]
        public void ResolveTaxClassId_Should_Return_Null_When_Neither_Tax_Class_Exists()
        {
            // execution
            Guid? result = TaxJarCalculation.ResolveTaxClassId(Guid.NewGuid(), [Guid.NewGuid()], Guid.NewGuid());

            // asserts
            result.Should().BeNull();
        }

        [Fact]
        public void BuildLineItem_Should_Map_Sku_Quantity_Tax_Code_Unit_Price_And_Discount()
        {
            // execution
            TaxLineItem item = TaxJarCalculation.BuildLineItem("SKU-1", 3m, "20010", 9.99m, 2.5m);

            // asserts
            item.Id.Should().Be("SKU-1");
            item.Quantity.Should().Be(3);
            item.ProductTaxCode.Should().Be("20010");
            item.UnitPrice.Should().Be(9.99m);
            item.Discount.Should().Be(2.5m);
        }

        [Fact]
        public void BuildLineItem_Should_Send_A_Fractional_Quantity_As_One_Unit_At_The_Line_Total()
        {
            // execution
            TaxLineItem item = TaxJarCalculation.BuildLineItem("SKU-1", 1.5m, "20010", 10m, 0m);

            // asserts
            item.Quantity.Should().Be(1);
            item.UnitPrice.Should().Be(15m);
        }

        [Fact]
        public void BuildLineItem_Should_Not_Truncate_A_Fractional_Quantity_Below_One()
        {
            // execution
            TaxLineItem item = TaxJarCalculation.BuildLineItem("SKU-1", 0.25m, null, 8m, 0m);

            // asserts
            (item.Quantity * item.UnitPrice).Should().Be(2m);
        }

        [Fact]
        public void BuildLineItem_Should_Leave_The_Tax_Code_Empty_When_There_Is_None()
        {
            // execution
            TaxLineItem item = TaxJarCalculation.BuildLineItem("SKU-1", 1m, null, 10m, 0m);

            // asserts
            item.ProductTaxCode.Should().BeNull();
        }

        [Fact]
        public void BuildTaxRequest_Should_Map_Both_Addresses_Subtotal_Shipping_And_Line_Items()
        {
            // preparation
            Address from = new("1 Warehouse Rd", null!, "Los Angeles", "CA", "US", "90001");
            Address to = new("2 Customer Ave", null!, "Beverly Hills", "CA", "US", "90210");
            List<TaxLineItem> lines =
            [
                TaxJarCalculation.BuildLineItem("SKU-1", 2m, "20010", 10m, 1m),
                TaxJarCalculation.BuildLineItem("SKU-2", 1m, null, 5m, 0m),
            ];

            // execution
            Tax request = TaxJarCalculation.BuildTaxRequest(from, to, 25m, 4.5m, lines);

            // asserts
            request.FromStreet.Should().Be("1 Warehouse Rd");
            request.FromCity.Should().Be("Los Angeles");
            request.FromState.Should().Be("CA");
            request.FromCountry.Should().Be("US");
            request.FromZip.Should().Be("90001");
            request.ToStreet.Should().Be("2 Customer Ave");
            request.ToCity.Should().Be("Beverly Hills");
            request.ToState.Should().Be("CA");
            request.ToCountry.Should().Be("US");
            request.ToZip.Should().Be("90210");
            request.Amount.Should().Be(25m);
            request.Shipping.Should().Be(4.5m);
            request.LineItems.Should().BeSameAs(lines);
        }
    }
}
