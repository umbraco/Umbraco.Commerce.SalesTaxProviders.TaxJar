using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using RestSharp;
using Taxjar;

namespace Umbraco.Commerce.SalesTaxProviders.TaxJar.UnitTests
{
    // Runs the real TaxJar SDK (request building, auth, serialization) against a stub HTTP handler.
    // The SDK keeps its RestClient in a private field, so the tests swap it for one backed by the stub and the SDK's own base URL.
    public class TaxjarApiHttpTests
    {
        private const string ApiUrl = "https://api.sandbox.taxjar.com";

        [Fact]
        public async Task TaxForOrderAsync_Should_Post_To_The_Taxes_Endpoint_On_The_Configured_Api_Url()
        {
            // preparation
            using StubHandler handler = new(TaxJarResponseMappingTests.TaxesResponseJson);
            TaxjarApi api = CreateApi(handler);

            // execution
            await api.TaxForOrderAsync(CreateRequest());

            // asserts
            handler.Requests.Should().ContainSingle();
            handler.Requests[0].Method.Should().Be(HttpMethod.Post);
            handler.Requests[0].Uri.Should().Be(new Uri(ApiUrl + "/v2/taxes"));
        }

        [Fact]
        public async Task TaxForOrderAsync_Should_Authenticate_With_The_Token_As_A_Bearer_Token()
        {
            // preparation
            using StubHandler handler = new(TaxJarResponseMappingTests.TaxesResponseJson);
            TaxjarApi api = CreateApi(handler);

            // execution
            await api.TaxForOrderAsync(CreateRequest());

            // asserts
            handler.Requests[0].Headers["Authorization"].Should().Be("Bearer test-token");
        }

        [Fact]
        public async Task TaxForOrderAsync_Should_Send_The_Request_As_A_Json_Body_With_Snake_Case_Fields()
        {
            // preparation
            using StubHandler handler = new(TaxJarResponseMappingTests.TaxesResponseJson);
            TaxjarApi api = CreateApi(handler);

            // execution
            await api.TaxForOrderAsync(CreateRequest());

            // asserts
            StubRequest sent = handler.Requests[0];
            sent.ContentType.Should().StartWith("application/json");

            using JsonDocument body = JsonDocument.Parse(sent.Body);
            JsonElement root = body.RootElement;
            root.GetProperty("from_street").GetString().Should().Be("1 Warehouse Rd");
            root.GetProperty("from_city").GetString().Should().Be("Los Angeles");
            root.GetProperty("from_state").GetString().Should().Be("CA");
            root.GetProperty("from_country").GetString().Should().Be("US");
            root.GetProperty("from_zip").GetString().Should().Be("90001");
            root.GetProperty("to_street").GetString().Should().Be("2 Customer Ave");
            root.GetProperty("to_city").GetString().Should().Be("Beverly Hills");
            root.GetProperty("to_state").GetString().Should().Be("CA");
            root.GetProperty("to_country").GetString().Should().Be("US");
            root.GetProperty("to_zip").GetString().Should().Be("90210");
            root.GetProperty("amount").GetDecimal().Should().Be(25m);
            root.GetProperty("shipping").GetDecimal().Should().Be(4.5m);

            JsonElement line = root.GetProperty("line_items")[0];
            line.GetProperty("id").GetString().Should().Be("SKU-1");
            line.GetProperty("quantity").GetInt32().Should().Be(2);
            line.GetProperty("product_tax_code").GetString().Should().Be("20010");
            line.GetProperty("unit_price").GetDecimal().Should().Be(10m);
            line.GetProperty("discount").GetDecimal().Should().Be(1m);
        }

        [Fact]
        public async Task TaxForOrderAsync_Should_Deserialize_The_Response_Into_Tax_Response_Attributes()
        {
            // preparation
            using StubHandler handler = new(TaxJarResponseMappingTests.TaxesResponseJson);
            TaxjarApi api = CreateApi(handler);

            // execution
            TaxResponseAttributes response = await api.TaxForOrderAsync(CreateRequest());

            // asserts
            response.AmountToCollect.Should().Be(1.35m);
            response.Jurisdictions.City.Should().Be("LOS ANGELES");
            response.Jurisdictions.County.Should().Be("LOS ANGELES COUNTY");
            response.Jurisdictions.State.Should().Be("CA");
            response.Jurisdictions.Country.Should().Be("US");
            response.Breakdown.CityTaxCollectable.Should().Be(0m);
            response.Breakdown.CountyTaxCollectable.Should().Be(0.15m);
            response.Breakdown.StateTaxCollectable.Should().Be(0.94m);
            response.Breakdown.CountryTaxCollectable.Should().Be(0m);
        }

        [Fact]
        public async Task TaxForOrderAsync_Should_Throw_When_TaxJar_Returns_An_Error_Status()
        {
            // preparation
            using StubHandler handler = new("""{"error":"Unauthorized","detail":"Bad token","status":401}""", HttpStatusCode.Unauthorized);
            TaxjarApi api = CreateApi(handler);

            // execution
            Func<Task> call = () => api.TaxForOrderAsync(CreateRequest());

            // asserts
            await call.Should().ThrowAsync<TaxjarException>();
        }

        [Fact]
        public void CreateClient_Should_Set_An_Explicit_Timeout_Instead_Of_RestSharps_100_Second_Default()
        {
            // execution
            TaxjarApi api = TaxJarSalesTaxProvider.CreateClient(new TaxJarSettings { TestMode = true, SandboxToken = "test-token" });

            // asserts
            api.timeout.Should().Be(15000);
        }

        [Theory]
        [InlineData(true, "https://api.sandbox.taxjar.com/v2/")]
        [InlineData(false, "https://api.taxjar.com/v2/")]
        public void CreateClient_Should_Use_The_Api_Url_For_The_Mode(bool testMode, string expectedBase)
        {
            // execution
            TaxjarApi api = TaxJarSalesTaxProvider.CreateClient(new TaxJarSettings { TestMode = testMode, SandboxToken = "sandbox-token", LiveToken = "live-token" });

            // asserts
            api.apiUrl.Should().Be(expectedBase);
        }

        [Fact]
        public async Task TaxForOrderAsync_Should_Fail_With_A_Timeout_The_Provider_Recognises_When_TaxJar_Is_Too_Slow()
        {
            // preparation
            using StubHandler handler = new(TaxJarResponseMappingTests.TaxesResponseJson) { Delay = TimeSpan.FromSeconds(30) };
            TaxjarApi api = CreateApi(handler);
            api.timeout = 100;

            // execution
            Func<Task> call = () => api.TaxForOrderAsync(CreateRequest());

            // asserts
            Exception thrown = (await call.Should().ThrowAsync<Exception>()).Which;
            TaxJarSalesTaxProvider.IsTimeout(thrown).Should().BeTrue();
        }

        [Fact]
        public async Task IsTimeout_Should_Be_False_For_A_TaxJar_Error_Response()
        {
            // preparation
            using StubHandler handler = new("""{"error":"Unauthorized","detail":"Bad token","status":401}""", HttpStatusCode.Unauthorized);
            TaxjarApi api = CreateApi(handler);

            // execution
            Func<Task> call = () => api.TaxForOrderAsync(CreateRequest());

            // asserts
            Exception thrown = (await call.Should().ThrowAsync<Exception>()).Which;
            TaxJarSalesTaxProvider.IsTimeout(thrown).Should().BeFalse();
        }

        private static TaxjarApi CreateApi(StubHandler handler)
        {
            // Constructs the client the way TaxJarSalesTaxProvider does
            TaxjarApi api = TaxJarSalesTaxProvider.CreateClient(new TaxJarSettings { TestMode = true, SandboxToken = "test-token" });

            FieldInfo apiClient = typeof(TaxjarApi).GetField("apiClient", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            RestClient original = (RestClient)apiClient.GetValue(api)!;
            apiClient.SetValue(api, new RestClient(handler, disposeHandler: false, configureRestClient: o => o.BaseUrl = original.Options.BaseUrl));
            return api;
        }

        private static Tax CreateRequest() => new()
        {
            FromStreet = "1 Warehouse Rd",
            FromCity = "Los Angeles",
            FromState = "CA",
            FromCountry = "US",
            FromZip = "90001",
            ToStreet = "2 Customer Ave",
            ToCity = "Beverly Hills",
            ToState = "CA",
            ToCountry = "US",
            ToZip = "90210",
            Amount = 25m,
            Shipping = 4.5m,
            LineItems =
            [
                TaxJarCalculation.BuildLineItem("SKU-1", 2m, "20010", 10m, -1m),
            ],
        };

        private sealed record StubRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? ContentType, string Body);

        private sealed class StubHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
        {
            public List<StubRequest> Requests { get; } = [];

            public TimeSpan Delay { get; init; } = TimeSpan.Zero;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
                }

                string body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                Dictionary<string, string> headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
                Requests.Add(new StubRequest(request.Method, request.RequestUri!, headers, request.Content?.Headers.ContentType?.ToString(), body));

                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
                };
            }
        }
    }
}
