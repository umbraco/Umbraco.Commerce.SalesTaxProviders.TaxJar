using Umbraco.Commerce.Core.SalesTaxProviders;

namespace Umbraco.Commerce.SalesTaxProviders.TaxJar
{
    public class TaxJarSettings
    {
        [SalesTaxProviderSetting(SortOrder = 100)]
        public string SandboxToken { get; set; }

        [SalesTaxProviderSetting(SortOrder = 200)]
        public string LiveToken { get; set; }

        [SalesTaxProviderSetting(SortOrder = 10000)]
        public bool TestMode { get; set; }
    }
}
